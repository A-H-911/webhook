using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Infrastructure.Redis;

/// <summary>
/// Reads WebhookRequest entries from the Redis Stream "webhook-requests",
/// persists each to SQL Server, and publishes the cross-process SSE pub/sub
/// notification. Uses consumer group semantics for at-least-once delivery
/// with a two-phase startup to recover pending (unACKed) messages.
/// </summary>
internal sealed class RedisStreamConsumerService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<RedisStreamConsumerService> logger) : BackgroundService
{
    private const string StreamKey = "webhook-requests";
    private const string ConsumerGroup = "webhook-api";
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

    private readonly string _consumerName = ResolveConsumerName();

    private static string ResolveConsumerName() =>
        Environment.GetEnvironmentVariable("WEBHOOK_WORKER_ID")
        ?? $"consumer-{Environment.MachineName}";

    private static readonly JsonSerializerOptions SseSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureConsumerGroupAsync(stoppingToken);

        // Phase 1: drain any unACKed messages from a previous run (PEL recovery).
        await DrainPendingAsync(stoppingToken);

        // Phase 2: poll for new messages continuously.
        logger.LogInformation(
            "Redis stream consumer {Consumer} started on {Stream}",
            _consumerName, StreamKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await redis.GetDatabase().StreamReadGroupAsync(
                    StreamKey, ConsumerGroup, _consumerName, ">",
                    count: BatchSize);

                if (entries is not { Length: > 0 })
                {
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                    await ProcessEntryAsync(entry, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                logger.LogWarning(ex, "Redis unavailable; pausing stream consumer for 5 s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in stream consumer; pausing 1 s before retry");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Redis stream consumer {Consumer} stopped", _consumerName);
    }

    private async Task DrainPendingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            StreamEntry[] pending;
            try
            {
                // "0-0" delivers messages owned by this consumer that were not yet ACKed.
                pending = await redis.GetDatabase().StreamReadGroupAsync(
                    StreamKey, ConsumerGroup, _consumerName, "0-0",
                    count: BatchSize);
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                logger.LogWarning(ex, "Redis unavailable during PEL drain; skipping");
                return;
            }

            if (pending is not { Length: > 0 })
                break;

            logger.LogInformation(
                "Recovering {Count} pending stream entries from previous run", pending.Length);

            foreach (var entry in pending)
                await ProcessEntryAsync(entry, ct);
        }
    }

    private async Task ProcessEntryAsync(StreamEntry entry, CancellationToken ct)
    {
        try
        {
            var payload = entry["payload"];
            if (!payload.HasValue)
            {
                logger.LogWarning("Stream entry {Id} has no payload field; skipping", entry.Id);
                await AckAsync(entry.Id);
                return;
            }

            var request = JsonSerializer.Deserialize<WebhookRequest>(payload.ToString());
            if (request is null)
            {
                logger.LogWarning("Stream entry {Id} deserialized to null; skipping", entry.Id);
                await AckAsync(entry.Id);
                return;
            }

            request.ProcessingTimeMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - request.ReceivedAt).TotalMilliseconds);

            await PersistAsync(request, ct);

            var summaryJson = BuildSummaryJson(request);
            await PublishSsePubSubAsync(request.TokenId, summaryJson);

            await AckAsync(entry.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave the entry unACKed so it re-enters the PEL and is recovered on next startup.
            logger.LogError(ex,
                "Failed to process stream entry {Id}; will retry on next startup", entry.Id);
        }
    }

    private async Task PersistAsync(WebhookRequest request, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookRequestRepository>();
        await repo.AddAsync(request, ct);
    }

    private async Task PublishSsePubSubAsync(Guid tokenId, string summaryJson)
    {
        try
        {
            var sub = redis.GetSubscriber();
            await sub.PublishAsync(
                RedisChannel.Literal($"sse:{tokenId}"),
                summaryJson);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis pub/sub publish failed for token {TokenId}", tokenId);
        }
    }

    private async Task AckAsync(RedisValue entryId)
    {
        try
        {
            await redis.GetDatabase().StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entryId);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis ACK failed for entry {EntryId}; will retry on next drain", entryId);
        }
    }

    private async Task EnsureConsumerGroupAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await redis.GetDatabase().StreamCreateConsumerGroupAsync(
                    StreamKey, ConsumerGroup,
                    position: StreamPosition.NewMessages,
                    createStream: true);

                logger.LogInformation(
                    "Created consumer group {Group} on stream {Stream}", ConsumerGroup, StreamKey);
                return;
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Group already exists — this is the normal restart path.
                logger.LogDebug(
                    "Consumer group {Group} already exists on stream {Stream}", ConsumerGroup, StreamKey);
                return;
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                logger.LogWarning(
                    ex, "Redis unavailable during consumer group setup (attempt {Attempt}/5)", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }

        logger.LogError(
            "Could not create consumer group after 5 attempts; stream consumer will not run");
    }

    private static string BuildSummaryJson(WebhookRequest r) =>
        JsonSerializer.Serialize(new
        {
            r.Id,
            r.TokenId,
            r.Method,
            r.Path,
            r.ReceivedAt,
            r.ContentType,
            r.SizeBytes,
            r.IpAddress
        }, SseSerializerOptions);
}

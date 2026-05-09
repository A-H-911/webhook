using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebhookService.Domain.Services;

namespace WebhookService.Infrastructure.Sse;

internal sealed class SseNotifier(
    IConnectionMultiplexer redis,
    ILogger<SseNotifier> logger) : ISseNotifier
{
    private const int MaxSubscribersPerToken = 10;
    private const int SubscriberTtlSeconds = 7200; // self-heals if instance crashes
    private const string CounterKeyPrefix = "wh:sse-count:";

    // Atomic INCR + limit check + EXPIRE; returns 0 on limit reached, new count on success.
    private const string TrySubscribeLua = """
        local count = redis.call('INCR', KEYS[1])
        if count > tonumber(ARGV[1]) then
            redis.call('DECR', KEYS[1])
            return 0
        end
        redis.call('EXPIRE', KEYS[1], tonumber(ARGV[2]))
        return count
        """;

    private readonly ConcurrentDictionary<Guid, List<Channel<SseEvent>>> _subscribers = new();
    private readonly Lock _lock = new();

    public async Task<bool> TrySubscribeAsync(Guid tokenId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var result = (long)await db.ScriptEvaluateAsync(
                TrySubscribeLua,
                new RedisKey[] { $"{CounterKeyPrefix}{tokenId}" },
                new RedisValue[] { MaxSubscribersPerToken, SubscriberTtlSeconds });
            return result > 0;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis SSE counter unavailable for token {TokenId}; using fail-open", tokenId);
            return true;
        }
    }

    public async Task UnsubscribeAsync(Guid tokenId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringDecrementAsync($"{CounterKeyPrefix}{tokenId}");
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis SSE counter DECR failed for token {TokenId}", tokenId);
        }
    }

    public async Task RefreshSubscriptionAsync(Guid tokenId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.KeyExpireAsync($"{CounterKeyPrefix}{tokenId}", TimeSpan.FromSeconds(SubscriberTtlSeconds));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis SSE counter EXPIRE failed for token {TokenId}", tokenId);
        }
    }

    public async IAsyncEnumerable<SseEvent> SubscribeAsync(
        Guid tokenId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<SseEvent>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        lock (_lock)
        {
            _subscribers.GetOrAdd(tokenId, _ => []).Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            lock (_lock)
            {
                if (_subscribers.TryGetValue(tokenId, out var list))
                {
                    list.Remove(channel);
                    if (list.Count == 0)
                        _subscribers.TryRemove(tokenId, out _);
                }
            }
        }
    }

    public Task NotifyAsync(Guid tokenId, string summaryJson, CancellationToken ct = default)
    {
        var evt = new SseEvent("request", summaryJson);
        List<Channel<SseEvent>> snapshot;

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(tokenId, out var list))
                return Task.CompletedTask;
            snapshot = [.. list];
        }

        // DropOldest mode means TryWrite never blocks; avoids ChannelClosedException
        // race if NotifyTokenDeleted completes the channel between snapshot and write.
        foreach (var channel in snapshot)
            channel.Writer.TryWrite(evt);

        return Task.CompletedTask;
    }

    public void NotifyTokenDeleted(Guid tokenId)
    {
        List<Channel<SseEvent>> snapshot;

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(tokenId, out var list))
                return;
            snapshot = [.. list];
            _subscribers.TryRemove(tokenId, out _);
        }

        var deletedEvt = new SseEvent("token-deleted", "{}");
        foreach (var channel in snapshot)
        {
            channel.Writer.TryWrite(deletedEvt);
            channel.Writer.TryComplete();
        }
    }
}

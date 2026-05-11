using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Hookbin.Domain.Services;

namespace Hookbin.Infrastructure.Redis;

/// <summary>
/// Subscribes to Redis Pub/Sub channel pattern "sse:{tokenId}" and forwards
/// messages to the in-process ISseNotifier so the Worker can trigger SSE
/// delivery across API instances.
/// </summary>
internal sealed class RedisSseBridgeService(
    IConnectionMultiplexer redis,
    ISseNotifier sseNotifier,
    ILogger<RedisSseBridgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();

        await sub.SubscribeAsync(
            RedisChannel.Pattern("sse:*"),
            (channel, message) =>
            {
                if (!message.HasValue)
                    return;

                // channel name format: "sse:{tokenId}"
                var channelStr = channel.ToString();
                var colonIndex = channelStr.IndexOf(':');
                if (colonIndex < 0 || colonIndex == channelStr.Length - 1)
                    return;

                if (!Guid.TryParse(channelStr.AsSpan(colonIndex + 1), out var tokenId))
                    return;

                var summaryJson = message.ToString();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sseNotifier.NotifyAsync(tokenId, summaryJson, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "SSE bridge notification failed for token {TokenId}", tokenId);
                    }
                }, stoppingToken);
            });

        logger.LogInformation("Redis SSE bridge subscribed to pattern sse:*");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — clean up the subscription
        }
        finally
        {
            await sub.UnsubscribeAsync(RedisChannel.Pattern("sse:*"));
        }
    }
}

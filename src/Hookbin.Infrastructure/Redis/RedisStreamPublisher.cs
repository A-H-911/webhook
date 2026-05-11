using System.Text.Json;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Services;

namespace Hookbin.Infrastructure.Redis;

internal sealed class RedisStreamPublisher(IConnectionMultiplexer redis) : IRequestQueuePublisher
{
    private const string StreamKey = "webhook-requests";
    private const int MaxStreamLength = 10_000;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<RedisConnectionException>()
                .Handle<RedisTimeoutException>(),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50)
        })
        .Build();

    public async Task PublishAsync(WebhookRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(request);
        var entries = new[] { new NameValueEntry("payload", payload) };

        await RetryPipeline.ExecuteAsync(async cancellationToken =>
        {
            var db = redis.GetDatabase();
            await db.StreamAddAsync(
                StreamKey,
                entries,
                messageId: null,
                maxLength: MaxStreamLength,
                useApproximateMaxLength: true);
        }, ct);
    }
}

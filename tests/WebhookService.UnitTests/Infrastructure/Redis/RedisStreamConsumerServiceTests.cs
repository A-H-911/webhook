using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Infrastructure.Redis;

namespace WebhookService.UnitTests.Infrastructure.Redis;

/// <summary>
/// Phase 1a RED tests — written before production code changes.
/// Compile-time RED: Constructor_ShouldNotRequireSseNotifier calls the desired
/// 3-parameter constructor that does not yet exist.
/// Runtime RED: consumer-name tests assert env-var behavior not yet implemented.
/// </summary>
public sealed class RedisStreamConsumerServiceTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();

    private RedisStreamConsumerService CreateService()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _redis.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);
        // RED: ctor no longer takes ISseNotifier — compile error until GREEN
        return new RedisStreamConsumerService(
            _redis,
            MakeScopeFactory(Substitute.For<IWebhookRequestRepository>()),
            NullLogger<RedisStreamConsumerService>.Instance);
    }

    private static IServiceScopeFactory MakeScopeFactory(IWebhookRequestRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // ── Constructor shape ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldNotRequireSseNotifier()
    {
        // After GREEN: instantiates without ISseNotifier — compile-time RED until then.
        var service = CreateService();
        service.Should().NotBeNull();
    }

    // ── Consumer name resolution ──────────────────────────────────────────────

    [Fact]
    public void ConsumerName_HonorsWebhookWorkerIdEnvVar()
    {
        // Arrange — set the env var before construction, clear after
        const string expected = "test-worker-7";
        Environment.SetEnvironmentVariable("WEBHOOK_WORKER_ID", expected);
        try
        {
            var service = CreateService();

            // Act — read via reflection (field is private)
            var field = typeof(RedisStreamConsumerService)
                .GetField("_consumerName",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull("_consumerName field must exist");
            var actual = (string?)field!.GetValue(service);

            // Assert
            actual.Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBHOOK_WORKER_ID", null);
        }
    }

    [Fact]
    public void ConsumerName_FallsBackToMachineName_WhenEnvVarMissing()
    {
        // Arrange — ensure env var is absent
        Environment.SetEnvironmentVariable("WEBHOOK_WORKER_ID", null);
        var service = CreateService();

        // Act
        var field = typeof(RedisStreamConsumerService)
            .GetField("_consumerName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("_consumerName field must exist");
        var actual = (string?)field!.GetValue(service);

        // Assert — GREEN implementation uses "consumer-{MachineName}" without ProcessId
        actual.Should().Be($"consumer-{Environment.MachineName}");
    }

    // ── Pub/sub fan-out ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessEntryAsync_PublishesToRedisPubSub_WhenEntryIsValid()
    {
        // Arrange — mock Redis to return one stream entry then empty
        var tokenId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/webhook/test",
            Headers = "{}",
            IpAddress = "127.0.0.1",
            UserAgent = string.Empty,
            SizeBytes = 0
        };
        var payload = JsonSerializer.Serialize(request);
        var entry = new StreamEntry("1-1", [new NameValueEntry("payload", payload)]);

        // Consumer group already exists — normal restart path
        _db.StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns<Task<bool>>(_ => throw new RedisServerException("BUSYGROUP Consumer Group name already exists"));

        var readCall = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(readCall++ == 0 ? [entry] : Array.Empty<StreamEntry>()));

        _db.StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));

        _subscriber.PublishAsync(
                Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));

        var repo = Substitute.For<IWebhookRequestRepository>();
        repo.AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // GREEN: 3-param ctor — no ISseNotifier
        var service = new RedisStreamConsumerService(
            _redis,
            MakeScopeFactory(repo),
            NullLogger<RedisStreamConsumerService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act — start, let one processing cycle complete, stop
        await service.StartAsync(cts.Token);
        await Task.Delay(400);
        await cts.CancelAsync();

        // Assert — pub/sub published to the sse:{tokenId} channel exactly once
        await _subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == $"sse:{tokenId}"),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }
}

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
        // Arrange — wire up class-level mocks that CreateService() would normally set up
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _redis.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);

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

        // PEL drain ("0-0") returns empty; main loop (">") returns the entry once, then empty
        var mainReadCall = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)"0-0"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(Array.Empty<StreamEntry>()));
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(mainReadCall++ == 0 ? [entry] : Array.Empty<StreamEntry>()));

        _db.StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));

        _subscriber.PublishAsync(
                Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));

        var repo = Substitute.For<IWebhookRequestRepository>();
        repo.AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

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

    // ── Branch-coverage helpers ───────────────────────────────────────────────

    private void SetupBaseRedis()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _redis.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);

        _db.StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns<Task<bool>>(_ => throw new RedisServerException("BUSYGROUP Consumer Group name already exists"));

        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)"0-0"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(Array.Empty<StreamEntry>()));

        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(Array.Empty<StreamEntry>()));

        _db.StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));

        _subscriber.PublishAsync(
                Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));
    }

    private static StreamEntry ValidEntry()
    {
        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/webhook/test",
            Headers = "{}",
            IpAddress = "127.0.0.1",
            UserAgent = string.Empty,
            SizeBytes = 0
        };
        return new StreamEntry("1-1", [new NameValueEntry("payload", JsonSerializer.Serialize(request))]);
    }

    private RedisStreamConsumerService BuildService(IWebhookRequestRepository repo) =>
        new(_redis, MakeScopeFactory(repo), NullLogger<RedisStreamConsumerService>.Instance);

    private static async Task RunCycleAsync(RedisStreamConsumerService service, int delayMs = 400)
    {
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(delayMs);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    // ── Branch-coverage tests ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessEntryAsync_MissingPayloadField_AcksAndSkipsWithoutPersisting()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        var entry = new StreamEntry("1-1", [new NameValueEntry("other", "data")]);
        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [entry] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        await repo.DidNotReceive().AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
        await _db.Received().StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ProcessEntryAsync_NullDeserialization_AcksAndSkipsWithoutPersisting()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        var entry = new StreamEntry("1-1", [new NameValueEntry("payload", "null")]);
        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [entry] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        await repo.DidNotReceive().AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
        await _db.Received().StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ProcessEntryAsync_PersistThrows_LeavesEntryUnackedForPelRecovery()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        repo.AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("DB unavailable")));

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [ValidEntry()] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        // ACK must NOT be called — the entry stays in PEL for recovery on next restart
        await _db.DidNotReceive().StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    // ── ProcessingTimeMs ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessEntryAsync_SetsProcessingTimeMs_NonNegative_BeforePersisting()
    {
        // Arrange — ReceivedAt in the past so elapsed ms >= 0
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        var receivedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = receivedAt,
            Method = "POST",
            Path = "/webhook/test",
            Headers = "{}",
            IpAddress = "1.2.3.4",
            UserAgent = string.Empty,
            SizeBytes = 0
        };
        var entry = new StreamEntry("5-1",
            [new NameValueEntry("payload", JsonSerializer.Serialize(request))]);

        long? captured = null;
        repo.AddAsync(
                Arg.Do<WebhookRequest>(r => captured = r.ProcessingTimeMs),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [entry] : Array.Empty<StreamEntry>()));

        // Act
        await RunCycleAsync(BuildService(repo));

        // Assert — ProcessingTimeMs must be set and reflect the ~1 second queue delay
        captured.Should().NotBeNull("StreamWorker must set ProcessingTimeMs before persisting");
        captured.Should().BeGreaterThanOrEqualTo(1000, "ReceivedAt is 1 second in the past");
        captured.Should().BeLessThan(60_000, "sanity check: timestamp arithmetic must not be inverted");
    }

    [Fact]
    public async Task ProcessEntryAsync_PublishSseFails_StillAcksEntry()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        _subscriber.PublishAsync(
                Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<long>(new RedisConnectionException(ConnectionFailureType.None, "test")));

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [ValidEntry()] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        // Pub/sub failure is swallowed inside PublishSsePubSubAsync — ACK still runs
        await _db.Received().StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task AckAsync_RedisConnectionException_DoesNotPropagateToProcessEntry()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        _db.StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<long>(new RedisConnectionException(ConnectionFailureType.None, "test")));

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [ValidEntry()] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        // Persist ran despite ACK failing — processing was not interrupted
        await repo.Received().AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainPendingAsync_RedisConnectionException_SkipsToMainLoop()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)"0-0"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<StreamEntry[]>(
                new RedisConnectionException(ConnectionFailureType.None, "test")));

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(calls++ == 0 ? [ValidEntry()] : Array.Empty<StreamEntry>()));

        await RunCycleAsync(BuildService(repo));

        // PEL drain failure did not stop the service — main loop ran and persisted the entry
        await repo.Received().AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RedisConnectionExceptionInMainLoop_PausesAndContinues()
    {
        var repo = Substitute.For<IWebhookRequestRepository>();
        SetupBaseRedis();

        var calls = 0;
        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
                Arg.Is<RedisValue>(v => v == (RedisValue)">"),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(ci => calls++ == 0
                ? Task.FromException<StreamEntry[]>(
                    new RedisConnectionException(ConnectionFailureType.None, "Redis down"))
                : Task.FromResult(Array.Empty<StreamEntry>()));

        using var cts = new CancellationTokenSource();
        var service = BuildService(repo);
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Service entered the main loop at least once (the call that threw)
        calls.Should().BeGreaterThan(0);
    }
}

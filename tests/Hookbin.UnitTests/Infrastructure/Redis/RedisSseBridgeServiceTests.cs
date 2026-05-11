using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Hookbin.Domain.Services;
using Hookbin.Infrastructure.Redis;

namespace Hookbin.UnitTests.Infrastructure.Redis;

public sealed class RedisSseBridgeServiceTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();
    private readonly ISseNotifier _sseNotifier = Substitute.For<ISseNotifier>();

    private RedisSseBridgeService CreateService()
    {
        _redis.GetSubscriber(Arg.Any<object?>()).Returns(_subscriber);
        return new RedisSseBridgeService(_redis, _sseNotifier, NullLogger<RedisSseBridgeService>.Instance);
    }

    // Starts the service and captures the subscribe callback for direct invocation in tests.
    private async Task<(RedisSseBridgeService service, Action<RedisChannel, RedisValue> callback)> StartAndCaptureAsync()
    {
        Action<RedisChannel, RedisValue>? captured = null;
        _subscriber
            .SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Do<Action<RedisChannel, RedisValue>>(cb => captured = cb),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50); // let ExecuteAsync reach SubscribeAsync in its background task

        captured.Should().NotBeNull("SubscribeAsync callback must be captured after startup");
        return (svc, captured!);
    }

    // ── Subscription lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_SubscribesToPattern_SseWildcard()
    {
        // Arrange
        _subscriber
            .SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);
        var svc = CreateService();

        // Act
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        // Assert
        await _subscriber.Received(1).SubscribeAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == "sse:*"),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromPattern_SseWildcard()
    {
        // Arrange
        _subscriber
            .SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);
        _subscriber
            .UnsubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>?>(), Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        // Act — StopAsync cancels stoppingToken; ExecuteAsync finally block runs UnsubscribeAsync
        await svc.StopAsync(CancellationToken.None);

        // Assert
        await _subscriber.Received(1).UnsubscribeAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == "sse:*"),
            Arg.Any<Action<RedisChannel, RedisValue>?>(),
            Arg.Any<CommandFlags>());
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_ForwardsMessage_WhenChannelAndValueAreValid()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        var summaryJson = @"{""id"":42}";
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sseNotifier
            .NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => { tcs.TrySetResult(); return Task.CompletedTask; });

        var (svc, callback) = await StartAndCaptureAsync();

        // Act
        callback(RedisChannel.Literal($"sse:{tokenId}"), (RedisValue)summaryJson);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert — correct tokenId and payload forwarded
        await _sseNotifier.Received(1).NotifyAsync(tokenId, summaryJson, Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Early-exit guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_Ignores_WhenChannelHasNoColon()
    {
        // Arrange
        var (svc, callback) = await StartAndCaptureAsync();

        // Act — no colon → colonIndex == -1 → early return before Task.Run
        callback(RedisChannel.Literal("ssefoo"), (RedisValue)@"{""id"":1}");
        await Task.Delay(50);

        // Assert
        await _sseNotifier.DidNotReceive().NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Callback_Ignores_WhenTokenIdIsNotGuid()
    {
        // Arrange
        var (svc, callback) = await StartAndCaptureAsync();

        // Act — "not-a-guid" fails Guid.TryParse → early return
        callback(RedisChannel.Literal("sse:not-a-guid"), (RedisValue)@"{""id"":1}");
        await Task.Delay(50);

        // Assert
        await _sseNotifier.DidNotReceive().NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Callback_Ignores_WhenMessageHasNoValue()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        var (svc, callback) = await StartAndCaptureAsync();

        // Act — RedisValue.Null has HasValue = false → early return
        callback(RedisChannel.Literal($"sse:{tokenId}"), RedisValue.Null);
        await Task.Delay(50);

        // Assert
        await _sseNotifier.DidNotReceive().NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Exception handling ────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SwallowsException_WhenNotifyAsyncThrows()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sseNotifier
            .NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                tcs.TrySetResult(); // signal invocation before returning the faulted task
                return Task.FromException(new InvalidOperationException("SSE failure"));
            });

        var (svc, callback) = await StartAndCaptureAsync();

        // Act — exception thrown inside Task.Run must be caught by the bridge's catch block
        callback(RedisChannel.Literal($"sse:{tokenId}"), (RedisValue)@"{""id"":1}");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(30); // let catch block run before asserting service is still alive

        // Assert — NotifyAsync was called and exception was swallowed (service still running)
        await _sseNotifier.Received(1).NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None);
    }
}

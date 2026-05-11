using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Hookbin.Domain.Services;
using Hookbin.Infrastructure.Sse;

namespace Hookbin.UnitTests.Infrastructure.Sse;

public sealed class SseNotifierTests
{
    private static (SseNotifier notifier, IDatabase db) CreateNotifier(long luaResult = 1)
    {
        var db = Substitute.For<IDatabase>();
        var muxer = Substitute.For<IConnectionMultiplexer>();
        var logger = NullLogger<SseNotifier>.Instance;
        muxer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(luaResult));
        return (new SseNotifier(muxer, logger), db);
    }

    // ── TrySubscribeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task TrySubscribeAsync_ReturnsTrue_WhenRedisReturnsPositiveCount()
    {
        var (notifier, _) = CreateNotifier(luaResult: 1);
        (await notifier.TrySubscribeAsync(Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public async Task TrySubscribeAsync_ReturnsFalse_WhenRedisReturnsZero()
    {
        var (notifier, _) = CreateNotifier(luaResult: 0);
        (await notifier.TrySubscribeAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task TrySubscribeAsync_ReturnsTrue_FailOpen_WhenRedisThrows()
    {
        var db = Substitute.For<IDatabase>();
        var muxer = Substitute.For<IConnectionMultiplexer>();
        var logger = NullLogger<SseNotifier>.Instance;
        muxer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis down"));
        var notifier = new SseNotifier(muxer, logger);

        (await notifier.TrySubscribeAsync(Guid.NewGuid())).Should().BeTrue();
    }

    // ── UnsubscribeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UnsubscribeAsync_DoesNotThrow_WhenRedisUnavailable()
    {
        var db = Substitute.For<IDatabase>();
        var muxer = Substitute.For<IConnectionMultiplexer>();
        var logger = NullLogger<SseNotifier>.Instance;
        muxer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.StringDecrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var notifier = new SseNotifier(muxer, logger);

        var act = async () => await notifier.UnsubscribeAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    // ── RefreshSubscriptionAsync ───────────────────────────────────────────

    [Fact]
    public async Task RefreshSubscriptionAsync_DoesNotThrow_WhenRedisUnavailable()
    {
        var db = Substitute.For<IDatabase>();
        var muxer = Substitute.For<IConnectionMultiplexer>();
        var logger = NullLogger<SseNotifier>.Instance;
        muxer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var notifier = new SseNotifier(muxer, logger);

        var act = async () => await notifier.RefreshSubscriptionAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    // ── NotifyAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAsync_DoesNotThrow_WhenNoSubscribers()
    {
        var (notifier, _) = CreateNotifier();
        var act = async () => await notifier.NotifyAsync(Guid.NewGuid(), "{}", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyAsync_DeliversSseEvent_ToSingleSubscriber()
    {
        var (notifier, _) = CreateNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        await notifier.TrySubscribeAsync(tokenId);
        var received = new List<SseEvent>();

        var consumer = Task.Run(async () =>
        {
            try { await foreach (var evt in notifier.SubscribeAsync(tokenId, cts.Token)) received.Add(evt); }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20);
        await notifier.NotifyAsync(tokenId, @"{""id"":1}", CancellationToken.None);
        await Task.Delay(50);
        await cts.CancelAsync();
        await consumer;

        received.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SseEvent("request", @"{""id"":1}"));
    }

    [Fact]
    public async Task NotifyAsync_DeliversSseEvent_ToMultipleSubscribers()
    {
        var (notifier, _) = CreateNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        // SemaphoreSlim lets each consumer signal once its channel is registered.
        // SubscribeAsync registers the channel synchronously inside the lock before
        // the first await, so calling MoveNextAsync() (without awaiting) is enough.
        var ready = new SemaphoreSlim(0, 2);

        await notifier.TrySubscribeAsync(tokenId);
        await notifier.TrySubscribeAsync(tokenId);

        var received1 = new List<SseEvent>();
        var received2 = new List<SseEvent>();

        async Task ConsumeAsync(List<SseEvent> received)
        {
            var e = notifier.SubscribeAsync(tokenId, cts.Token).GetAsyncEnumerator(cts.Token);
            var firstMove = e.MoveNextAsync(); // runs synchronously through lock → channel registered → suspends
            ready.Release();
            try
            {
                if (await firstMove) received.Add(e.Current);
                while (await e.MoveNextAsync()) received.Add(e.Current);
            }
            catch (OperationCanceledException) { }
            finally { await e.DisposeAsync(); }
        }

        var c1 = Task.Run(() => ConsumeAsync(received1));
        var c2 = Task.Run(() => ConsumeAsync(received2));

        await ready.WaitAsync();
        await ready.WaitAsync();

        await notifier.NotifyAsync(tokenId, "payload", CancellationToken.None);
        await Task.Delay(100);
        await cts.CancelAsync();
        await Task.WhenAll(c1, c2);

        received1.Should().ContainSingle();
        received2.Should().ContainSingle();
    }

    // ── NotifyTokenDeleted ───────────────────────────────────────────────────

    [Fact]
    public void NotifyTokenDeleted_DoesNotThrow_WhenNoSubscribers()
    {
        var (notifier, _) = CreateNotifier();
        var act = () => notifier.NotifyTokenDeleted(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public async Task NotifyTokenDeleted_CompletesChannel_SoConsumerExitsCleanly()
    {
        var (notifier, _) = CreateNotifier();
        var tokenId = Guid.NewGuid();

        await notifier.TrySubscribeAsync(tokenId);
        var received = new List<SseEvent>();

        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in notifier.SubscribeAsync(tokenId, CancellationToken.None))
                received.Add(evt);
        });

        await Task.Delay(20);
        notifier.NotifyTokenDeleted(tokenId);

        var finished = await Task.WhenAny(consumer, Task.Delay(2000));
        finished.Should().Be(consumer, "consumer must exit after token-deleted completes the channel");
        received.Should().ContainSingle(e => e.EventName == "token-deleted");
    }

    // ── SubscribeAsync cleanup ───────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_RemovesChannelFromList_OnCancellation()
    {
        var (notifier, _) = CreateNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        await notifier.TrySubscribeAsync(tokenId);

        var consumer = Task.Run(async () =>
        {
            try { await foreach (var _ in notifier.SubscribeAsync(tokenId, cts.Token)) { } }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20);
        await cts.CancelAsync();
        await consumer;

        var act = async () => await notifier.NotifyAsync(tokenId, "{}", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}

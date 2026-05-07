using FluentAssertions;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure.Sse;

namespace WebhookService.UnitTests.Infrastructure.Sse;

public sealed class SseNotifierTests
{
    // ── TrySubscribe / Unsubscribe ──────────────────────────────────────────

    [Fact]
    public void TrySubscribe_ReturnsTrueForFirstSubscriber()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        notifier.TrySubscribe(tokenId).Should().BeTrue();
    }

    [Fact]
    public void TrySubscribe_ReturnsTrueUpToMaxSubscribers()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            notifier.TrySubscribe(tokenId).Should().BeTrue($"subscriber {i + 1} of 10 should succeed");
    }

    [Fact]
    public void TrySubscribe_ReturnsFalse_WhenAtMaxSubscribers()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            notifier.TrySubscribe(tokenId);

        notifier.TrySubscribe(tokenId).Should().BeFalse();
    }

    [Fact]
    public void TrySubscribe_AllowsNewSubscription_AfterUnsubscribe()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            notifier.TrySubscribe(tokenId);

        notifier.Unsubscribe(tokenId);

        notifier.TrySubscribe(tokenId).Should().BeTrue();
    }

    [Fact]
    public void Unsubscribe_DoesNotThrow_WhenNoPriorSubscription()
    {
        var notifier = new SseNotifier();

        var act = () => notifier.Unsubscribe(Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void Unsubscribe_CleansUpEntry_WhenLastSubscriberLeaves()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        notifier.TrySubscribe(tokenId);
        notifier.Unsubscribe(tokenId);

        // Count entry removed; a fresh TrySubscribe must succeed (count starts back at 0)
        notifier.TrySubscribe(tokenId).Should().BeTrue();
    }

    // ── NotifyAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAsync_DoesNotThrow_WhenNoSubscribers()
    {
        var notifier = new SseNotifier();

        var act = async () => await notifier.NotifyAsync(Guid.NewGuid(), "{}", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyAsync_DeliversSseEvent_ToSingleSubscriber()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        notifier.TrySubscribe(tokenId);
        var received = new List<SseEvent>();

        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in notifier.SubscribeAsync(tokenId, cts.Token))
                    received.Add(evt);
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20); // ensure consumer is listening
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
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        notifier.TrySubscribe(tokenId);
        notifier.TrySubscribe(tokenId);

        var received1 = new List<SseEvent>();
        var received2 = new List<SseEvent>();

        var c1 = Task.Run(async () =>
        {
            try { await foreach (var evt in notifier.SubscribeAsync(tokenId, cts.Token)) received1.Add(evt); }
            catch (OperationCanceledException) { }
        });
        var c2 = Task.Run(async () =>
        {
            try { await foreach (var evt in notifier.SubscribeAsync(tokenId, cts.Token)) received2.Add(evt); }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20);
        await notifier.NotifyAsync(tokenId, "payload", CancellationToken.None);
        await Task.Delay(50);
        await cts.CancelAsync();
        await Task.WhenAll(c1, c2);

        received1.Should().ContainSingle();
        received2.Should().ContainSingle();
    }

    // ── NotifyTokenDeleted ───────────────────────────────────────────────────

    [Fact]
    public void NotifyTokenDeleted_DoesNotThrow_WhenNoSubscribers()
    {
        var notifier = new SseNotifier();

        var act = () => notifier.NotifyTokenDeleted(Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task NotifyTokenDeleted_CompletesChannel_SoConsumerExitsCleanly()
    {
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();

        notifier.TrySubscribe(tokenId);
        var received = new List<SseEvent>();

        // No external cancellation — consumer must exit because the channel is completed
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
        var notifier = new SseNotifier();
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        notifier.TrySubscribe(tokenId);

        var consumer = Task.Run(async () =>
        {
            try { await foreach (var _ in notifier.SubscribeAsync(tokenId, cts.Token)) { } }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20);
        await cts.CancelAsync();
        await consumer;

        // Channel was cleaned up — a subsequent NotifyAsync for the same token must not throw
        var act = async () => await notifier.NotifyAsync(tokenId, "{}", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}

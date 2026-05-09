using System.Runtime.CompilerServices;
using WebhookService.Domain.Services;

namespace WebhookService.IntegrationTests;

internal sealed class TestNullSseNotifier : ISseNotifier
{
    public async IAsyncEnumerable<SseEvent> SubscribeAsync(
        Guid tokenId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task NotifyAsync(Guid tokenId, string summaryJson, CancellationToken ct = default)
        => Task.CompletedTask;

    public void NotifyTokenDeleted(Guid tokenId) { }

    public Task<bool> TrySubscribeAsync(Guid tokenId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task UnsubscribeAsync(Guid tokenId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RefreshSubscriptionAsync(Guid tokenId, CancellationToken ct = default)
        => Task.CompletedTask;
}

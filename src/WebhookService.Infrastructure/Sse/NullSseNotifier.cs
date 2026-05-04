using WebhookService.Domain.Services;

namespace WebhookService.Infrastructure.Sse;

internal sealed class NullSseNotifier : ISseNotifier
{
    public IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct)
        => AsyncEnumerable.Empty<SseEvent>();

    public Task NotifyAsync(Guid tokenId, string summaryJson)
        => Task.CompletedTask;

    public void NotifyTokenDeleted(Guid tokenId) { }
}

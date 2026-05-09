namespace WebhookService.Domain.Services;

public interface ISseNotifier
{
    IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct);
    Task NotifyAsync(Guid tokenId, string summaryJson, CancellationToken ct = default);
    void NotifyTokenDeleted(Guid tokenId);
    Task<bool> TrySubscribeAsync(Guid tokenId, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid tokenId, CancellationToken ct = default);
    Task RefreshSubscriptionAsync(Guid tokenId, CancellationToken ct = default);
}

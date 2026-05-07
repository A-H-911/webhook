namespace WebhookService.Domain.Services;

public interface ISseNotifier
{
    IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct);
    Task NotifyAsync(Guid tokenId, string summaryJson, CancellationToken ct = default);
    void NotifyTokenDeleted(Guid tokenId);
    bool TrySubscribe(Guid tokenId);
    void Unsubscribe(Guid tokenId);
}

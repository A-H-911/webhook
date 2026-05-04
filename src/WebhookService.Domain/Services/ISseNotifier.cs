namespace WebhookService.Domain.Services;

public interface ISseNotifier
{
    IAsyncEnumerable<SseEvent> SubscribeAsync(Guid tokenId, CancellationToken ct);
    Task NotifyAsync(Guid tokenId, string summaryJson);
    void NotifyTokenDeleted(Guid tokenId);
}

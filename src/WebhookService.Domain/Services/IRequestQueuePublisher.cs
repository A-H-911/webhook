using WebhookService.Domain.Entities;

namespace WebhookService.Domain.Services;

public interface IRequestQueuePublisher
{
    Task PublishAsync(WebhookRequest request, CancellationToken ct = default);
}

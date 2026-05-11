using Hookbin.Domain.Entities;

namespace Hookbin.Domain.Services;

public interface IRequestQueuePublisher
{
    Task PublishAsync(WebhookRequest request, CancellationToken ct = default);
}

using WebhookService.Domain.Entities;

namespace WebhookService.Domain.Repositories;

public interface IWebhookTokenRepository
{
    Task<WebhookToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WebhookToken?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookToken>> GetAllActiveAsync(CancellationToken ct = default);
    Task AddAsync(WebhookToken token, CancellationToken ct = default);
    Task UpdateAsync(WebhookToken token, CancellationToken ct = default);
}

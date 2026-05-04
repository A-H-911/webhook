using WebhookService.Domain.Entities;

namespace WebhookService.Domain.Repositories;

public interface IWebhookRequestRepository
{
    Task<WebhookRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<WebhookRequest> Items, int Total)> GetPagedAsync(
        Guid tokenId, int page, int pageSize, string? search, CancellationToken ct = default);
    Task AddAsync(WebhookRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task DeleteAllForTokenAsync(Guid tokenId, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}

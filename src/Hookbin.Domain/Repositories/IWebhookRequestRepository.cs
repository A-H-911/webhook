using Hookbin.Domain.Entities;

namespace Hookbin.Domain.Repositories;

public interface IWebhookRequestRepository
{
    Task<WebhookRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<WebhookRequest> Items, int Total)> GetPagedAsync(
        Guid tokenId, int page, int pageSize, string? search,
        string[]? methods = null, int[]? statusGroups = null,
        CancellationToken ct = default);
    Task AddAsync(WebhookRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task DeleteAllForTokenAsync(Guid tokenId, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    Task<bool> UpdateNoteAsync(Guid id, Guid tokenId, string? note, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int[]>> GetSparklineBatchAsync(
        IEnumerable<Guid> tokenIds, CancellationToken ct = default);
}

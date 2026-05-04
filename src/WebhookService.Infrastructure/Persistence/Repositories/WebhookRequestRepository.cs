using Microsoft.EntityFrameworkCore;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Infrastructure.Persistence.Repositories;

internal sealed class WebhookRequestRepository : IWebhookRequestRepository
{
    private readonly ApplicationDbContext _db;

    public WebhookRequestRepository(ApplicationDbContext db) => _db = db;

    public Task<WebhookRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.WebhookRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(IReadOnlyList<WebhookRequest> Items, int Total)> GetPagedAsync(
        Guid tokenId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _db.WebhookRequests.Where(r => r.TokenId == tokenId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r =>
                (r.Headers != null && r.Headers.Contains(search)) ||
                (r.Body != null && r.Body.Contains(search)));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddAsync(WebhookRequest request, CancellationToken ct = default)
    {
        await _db.WebhookRequests.AddAsync(request, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.WebhookRequests.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllForTokenAsync(Guid tokenId, CancellationToken ct = default)
    {
        await _db.WebhookRequests.Where(r => r.TokenId == tokenId).ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        return await _db.WebhookRequests
            .Where(r => r.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}

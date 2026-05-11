using Microsoft.EntityFrameworkCore;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.Infrastructure.Persistence.Repositories;

internal sealed class WebhookRequestRepository : IWebhookRequestRepository
{
    private readonly ApplicationDbContext _db;

    public WebhookRequestRepository(ApplicationDbContext db) => _db = db;

    public Task<WebhookRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.WebhookRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(IReadOnlyList<WebhookRequest> Items, int Total)> GetPagedAsync(
        Guid tokenId, int page, int pageSize, string? search,
        string[]? methods = null, int[]? statusGroups = null,
        CancellationToken ct = default)
    {
        var query = _db.WebhookRequests.AsNoTracking().Where(r => r.TokenId == tokenId);

        if (!string.IsNullOrWhiteSpace(search) && search.Length >= 2)
            query = query.Where(r =>
                r.Method == search ||
                r.Path.Contains(search) ||
                (r.IpAddress != null && r.IpAddress.Contains(search)));

        if (methods is { Length: > 0 })
            query = query.Where(r => methods.Contains(r.Method));

        if (statusGroups is { Length: > 0 })
            query = query.Where(r => statusGroups.Any(g =>
                r.ResponseStatusCode >= g * 100 && r.ResponseStatusCode < (g + 1) * 100));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.ReceivedAt)
            .ThenByDescending(r => r.Id)
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

    public async Task<bool> UpdateNoteAsync(Guid id, Guid tokenId, string? note, CancellationToken ct = default)
    {
        var affected = await _db.WebhookRequests
            .Where(r => r.Id == id && r.TokenId == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Note, note), ct);
        return affected > 0;
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        const int batchSize = 1000;
        var total = 0;
        int deleted;
        do
        {
            deleted = await _db.WebhookRequests
                .Where(r => r.ReceivedAt < cutoff)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
        }
        while (deleted == batchSize);
        return total;
    }

    public async Task<IReadOnlyDictionary<Guid, int[]>> GetSparklineBatchAsync(
        IEnumerable<Guid> tokenIds, CancellationToken ct = default)
    {
        var idList = tokenIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<Guid, int[]>();

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);

        var bins = await _db.WebhookRequests
            .AsNoTracking()
            .Where(r => idList.Contains(r.TokenId) && r.ReceivedAt >= cutoff)
            .GroupBy(r => new
            {
                r.TokenId,
                Bucket = EF.Functions.DateDiffHour(r.ReceivedAt, now)
            })
            .Select(g => new { g.Key.TokenId, g.Key.Bucket, Count = g.Count() })
            .ToListAsync(ct);

        var result = idList.ToDictionary(id => id, _ => new int[24]);

        foreach (var bin in bins)
        {
            if (bin.Bucket >= 0 && bin.Bucket <= 23)
                result[bin.TokenId][23 - bin.Bucket] = bin.Count;
        }

        return result;
    }
}

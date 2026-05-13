using Microsoft.EntityFrameworkCore;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.Infrastructure.Persistence.Repositories;

internal sealed class WebhookTokenRepository : IWebhookTokenRepository
{
    private readonly ApplicationDbContext _db;

    public WebhookTokenRepository(ApplicationDbContext db) => _db = db;

    public Task<WebhookToken?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct);

    public Task<WebhookToken?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token && t.IsActive, ct);

    public Task<WebhookToken?> GetByTokenIncludingInactiveAsync(Guid token, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token, ct);

    public Task<WebhookToken?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<WebhookToken>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.WebhookTokens
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync(ct);

    public async Task AddAsync(WebhookToken token, CancellationToken ct = default)
    {
        await _db.WebhookTokens.AddAsync(token, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WebhookToken token, CancellationToken ct = default)
    {
        var tracked = await _db.WebhookTokens.FindAsync([token.Id], ct);
        if (tracked is null) return;

        tracked.UpdateName(token.Name);
        tracked.UpdateDescription(token.Description);
        if (token.IsActive) tracked.Activate(); else tracked.Deactivate();
        if (token.CustomResponse is not null)
            tracked.SetCustomResponse(token.CustomResponse);
        else
            tracked.ClearCustomResponse();

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tracked = await _db.WebhookTokens.FindAsync([id], ct);
        if (tracked is null) return;
        _db.WebhookTokens.Remove(tracked);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<TokenPageRow> Items, int Total)> GetPagedWithStatsAsync(
        int skip, int take, CancellationToken ct = default)
    {
        var cutoff24h = DateTimeOffset.UtcNow.AddHours(-24);
        var query = _db.WebhookTokens.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(t => new
            {
                Token = t,
                LifetimeCount = _db.WebhookRequests.Count(r => r.TokenId == t.Id),
                Count24h = _db.WebhookRequests.Count(r => r.TokenId == t.Id && r.ReceivedAt >= cutoff24h),
                LastReceivedAt = _db.WebhookRequests
                    .Where(r => r.TokenId == t.Id)
                    .Max(r => (DateTimeOffset?)r.ReceivedAt)
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new TokenPageRow(r.Token, r.LifetimeCount, r.Count24h, r.LastReceivedAt))
            .ToList();

        return (items, total);
    }

    public async Task<DashboardMetrics> GetDashboardMetricsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff7d = now.AddDays(-7);
        var cutoff24h = now.AddHours(-24);
        var cutoff5min = now.AddMinutes(-5);

        var totalEndpoints = await _db.WebhookTokens.CountAsync(t => t.IsActive, ct);
        var newEndpointsLast7d = await _db.WebhookTokens.CountAsync(t => t.IsActive && t.CreatedAt >= cutoff7d, ct);
        var requestsAllTime = await _db.WebhookRequests.LongCountAsync(ct);
        var requestsLast24h = await _db.WebhookRequests.LongCountAsync(r => r.ReceivedAt >= cutoff24h, ct);
        var liveEndpoints = await _db.WebhookRequests
            .Where(r => r.ReceivedAt >= cutoff5min)
            .Select(r => r.TokenId)
            .Distinct()
            .CountAsync(ct);

        return new DashboardMetrics(totalEndpoints, newEndpointsLast7d, requestsAllTime, requestsLast24h, liveEndpoints);
    }
}

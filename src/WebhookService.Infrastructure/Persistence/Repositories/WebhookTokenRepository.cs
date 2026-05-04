using Microsoft.EntityFrameworkCore;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Infrastructure.Persistence.Repositories;

internal sealed class WebhookTokenRepository : IWebhookTokenRepository
{
    private readonly ApplicationDbContext _db;

    public WebhookTokenRepository(ApplicationDbContext db) => _db = db;

    public Task<WebhookToken?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct);

    public Task<WebhookToken?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => _db.WebhookTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token && t.IsActive, ct);

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

        tracked.Description = token.Description;
        tracked.IsActive = token.IsActive;
        tracked.CustomResponse = token.CustomResponse;

        await _db.SaveChangesAsync(ct);
    }
}

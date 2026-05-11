using Hookbin.Domain.Entities;

namespace Hookbin.Domain.Repositories;

public interface IWebhookTokenRepository
{
    Task<WebhookToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WebhookToken?> GetByTokenAsync(Guid token, CancellationToken ct = default);

    // Receive path only — does not filter IsActive so inactive tokens still capture requests.
    Task<WebhookToken?> GetByTokenIncludingInactiveAsync(Guid token, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookToken>> GetAllActiveAsync(CancellationToken ct = default);
    Task<(IReadOnlyList<TokenPageRow> Items, int Total)> GetPagedWithStatsAsync(
        int skip, int take, CancellationToken ct = default);
    Task<DashboardMetrics> GetDashboardMetricsAsync(CancellationToken ct = default);
    Task AddAsync(WebhookToken token, CancellationToken ct = default);
    Task UpdateAsync(WebhookToken token, CancellationToken ct = default);
}

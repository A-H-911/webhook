using MediatR;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Dashboard.Queries.GetDashboardMetrics;

internal sealed class GetDashboardMetricsQueryHandler(IWebhookTokenRepository repository)
    : IRequestHandler<GetDashboardMetricsQuery, DashboardMetrics>
{
    public Task<DashboardMetrics> Handle(GetDashboardMetricsQuery request, CancellationToken cancellationToken)
        => repository.GetDashboardMetricsAsync(cancellationToken);
}

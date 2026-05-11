using MediatR;
using Hookbin.Domain.Entities;

namespace Hookbin.Application.Dashboard.Queries.GetDashboardMetrics;

public sealed record GetDashboardMetricsQuery : IRequest<DashboardMetrics>;

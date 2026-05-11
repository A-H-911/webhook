namespace Hookbin.Domain.Entities;

public sealed record DashboardMetrics(
    int TotalEndpoints,
    int NewEndpointsLast7d,
    long RequestsCapturedAllTime,
    long RequestsCapturedLast24h,
    int LiveEndpoints);

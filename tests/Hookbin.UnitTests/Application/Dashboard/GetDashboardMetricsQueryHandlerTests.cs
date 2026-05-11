using FluentAssertions;
using NSubstitute;
using Hookbin.Application.Dashboard.Queries.GetDashboardMetrics;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Dashboard;

public sealed class GetDashboardMetricsQueryHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();

    [Fact]
    public async Task Handle_ReturnsDashboardMetrics_FromRepository()
    {
        var expected = new DashboardMetrics(
            TotalEndpoints: 10,
            NewEndpointsLast7d: 2,
            RequestsCapturedAllTime: 500,
            RequestsCapturedLast24h: 42,
            LiveEndpoints: 3);
        _repo.GetDashboardMetricsAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetDashboardMetricsQueryHandler(_repo);
        var result = await handler.Handle(new GetDashboardMetricsQuery(), CancellationToken.None);

        result.Should().Be(expected);
        result.TotalEndpoints.Should().Be(10);
        result.NewEndpointsLast7d.Should().Be(2);
        result.RequestsCapturedAllTime.Should().Be(500);
        result.RequestsCapturedLast24h.Should().Be(42);
        result.LiveEndpoints.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ReturnsZeroMetrics_WhenNoData()
    {
        var empty = new DashboardMetrics(0, 0, 0, 0, 0);
        _repo.GetDashboardMetricsAsync(Arg.Any<CancellationToken>()).Returns(empty);

        var handler = new GetDashboardMetricsQueryHandler(_repo);
        var result = await handler.Handle(new GetDashboardMetricsQuery(), CancellationToken.None);

        result.TotalEndpoints.Should().Be(0);
        result.LiveEndpoints.Should().Be(0);
        result.RequestsCapturedAllTime.Should().Be(0);
    }
}

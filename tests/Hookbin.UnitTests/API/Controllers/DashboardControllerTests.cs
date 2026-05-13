using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Hookbin.API.Controllers;
using Hookbin.Application.Dashboard.Queries.GetDashboardMetrics;
using Hookbin.Domain.Entities;

namespace Hookbin.UnitTests.API.Controllers;

public sealed class DashboardControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private DashboardController CreateController()
    {
        var controller = new DashboardController(_mediator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task GetMetrics_ReturnsOk_WithMediatorPayload()
    {
        var metrics = new DashboardMetrics(
            TotalEndpoints: 5,
            NewEndpointsLast7d: 2,
            RequestsCapturedAllTime: 1500,
            RequestsCapturedLast24h: 30,
            LiveEndpoints: 4);
        _mediator.Send(Arg.Any<GetDashboardMetricsQuery>(), Arg.Any<CancellationToken>()).Returns(metrics);

        var result = await CreateController().GetMetrics(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(metrics);
    }

    [Fact]
    public async Task GetMetrics_SendsGetDashboardMetricsQuery_Once()
    {
        _mediator.Send(Arg.Any<GetDashboardMetricsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardMetrics(0, 0, 0, 0, 0));

        await CreateController().GetMetrics(CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Any<GetDashboardMetricsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMetrics_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;
        _mediator.Send(Arg.Any<GetDashboardMetricsQuery>(), Arg.Do<CancellationToken>(t => captured = t))
            .Returns(new DashboardMetrics(0, 0, 0, 0, 0));

        await CreateController().GetMetrics(cts.Token);

        captured.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetMetrics_ReturnsEmptyShape_WhenNoData()
    {
        var empty = new DashboardMetrics(0, 0, 0, 0, 0);
        _mediator.Send(Arg.Any<GetDashboardMetricsQuery>(), Arg.Any<CancellationToken>()).Returns(empty);

        var result = await CreateController().GetMetrics(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DashboardMetrics>().Which.TotalEndpoints.Should().Be(0);
    }
}

using MediatR;
using Microsoft.AspNetCore.Mvc;
using Hookbin.Application.Dashboard.Queries.GetDashboardMetrics;

namespace Hookbin.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[AutoValidateAntiforgeryToken]
public sealed class DashboardController(IMediator mediator) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var metrics = await mediator.Send(new GetDashboardMetricsQuery(), ct);
        return Ok(metrics);
    }
}

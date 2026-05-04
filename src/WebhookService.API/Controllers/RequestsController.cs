using MediatR;
using Microsoft.AspNetCore.Mvc;
using WebhookService.Application.Requests.Commands.ClearRequests;
using WebhookService.Application.Requests.Commands.DeleteRequest;
using WebhookService.Application.Requests.Queries.ExportRequest;
using WebhookService.Application.Requests.Queries.GetRequestById;
using WebhookService.Application.Requests.Queries.GetRequests;

namespace WebhookService.API.Controllers;

[ApiController]
[Route("api/tokens/{tokenId:guid}/requests")]
public sealed class RequestsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPaged(
        Guid tokenId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetRequestsQuery(tokenId, page, pageSize, search), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid tokenId, Guid id, CancellationToken ct)
    {
        var request = await mediator.Send(new GetRequestByIdQuery(tokenId, id), ct);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid tokenId, Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new ExportRequestQuery(tokenId, id), ct);
        if (result is null) return NotFound();
        return File(result, "application/json", $"request-{id}.json");
    }

    [HttpDelete]
    public async Task<IActionResult> ClearAll(Guid tokenId, CancellationToken ct)
    {
        await mediator.Send(new ClearRequestsCommand(tokenId), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid tokenId, Guid id, CancellationToken ct)
    {
        var deleted = await mediator.Send(new DeleteRequestCommand(tokenId, id), ct);
        return deleted ? NoContent() : NotFound();
    }
}

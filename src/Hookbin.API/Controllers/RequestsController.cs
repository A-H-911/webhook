using MediatR;
using Microsoft.AspNetCore.Mvc;
using Hookbin.Application.Requests.Commands.ClearRequests;
using Hookbin.Application.Requests.Commands.DeleteRequest;
using Hookbin.Application.Requests.Commands.SetRequestNote;
using Hookbin.Application.Requests.Queries.ExportRequest;
using Hookbin.Application.Requests.Queries.GetRequestById;
using Hookbin.Application.Requests.Queries.GetRequests;

namespace Hookbin.API.Controllers;

[ApiController]
[Route("api/tokens/{tokenId:guid}/requests")]
[AutoValidateAntiforgeryToken]
public sealed class RequestsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPaged(
        Guid tokenId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string[]? methods = null,
        [FromQuery] int[]? statusGroups = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetRequestsQuery(tokenId, page, pageSize, search, methods, statusGroups), ct);
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

    [HttpPatch("{id:guid}/note")]
    public async Task<IActionResult> SetNote(
        Guid tokenId, Guid id, [FromBody] SetNoteRequest body, CancellationToken ct)
    {
        var found = await mediator.Send(new SetRequestNoteCommand(tokenId, id, body.Note), ct);
        return found ? NoContent() : NotFound();
    }
}

public sealed record SetNoteRequest(string? Note);

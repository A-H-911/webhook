using MediatR;
using Microsoft.AspNetCore.Mvc;
using Hookbin.Application.Tokens.Commands.CreateToken;
using Hookbin.Application.Tokens.Commands.DeleteToken;
using Hookbin.Application.Tokens.Commands.ResetCustomResponse;
using Hookbin.Application.Tokens.Commands.SetCustomResponse;
using Hookbin.Application.Tokens.Commands.UpdateToken;
using Hookbin.Application.Tokens.Queries.GetToken;
using Hookbin.Application.Tokens.Queries.GetTokens;

namespace Hookbin.API.Controllers;

[ApiController]
[Route("api/tokens")]
[AutoValidateAntiforgeryToken]
public sealed class TokensController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetTokensQuery(skip, take), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var token = await mediator.Send(new GetTokenQuery(id), ct);
        return token is null ? NotFound() : Ok(token);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTokenRequest body, CancellationToken ct)
    {
        var token = await mediator.Send(new CreateTokenCommand(body.Name, body.Description), ct);
        return CreatedAtAction(nameof(GetById), new { id = token.Id }, token);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTokenRequest body, CancellationToken ct)
    {
        var token = await mediator.Send(new UpdateTokenCommand(id, body.Name, body.Description, body.IsActive), ct);
        return token is null ? NotFound() : Ok(token);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await mediator.Send(new DeleteTokenCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/custom-response")]
    public async Task<IActionResult> SetCustomResponse(Guid id, [FromBody] SetCustomResponseRequest body, CancellationToken ct)
    {
        var updated = await mediator.Send(
            new SetCustomResponseCommand(id, body.StatusCode, body.ContentType, body.Body, body.Headers), ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}/custom-response")]
    public async Task<IActionResult> ResetCustomResponse(Guid id, CancellationToken ct)
    {
        var found = await mediator.Send(new ResetCustomResponseCommand(id), ct);
        return found ? NoContent() : NotFound();
    }
}

public sealed record CreateTokenRequest(string Name, string? Description);
public sealed record UpdateTokenRequest(string Name, string? Description, bool IsActive);
public sealed record SetCustomResponseRequest(int StatusCode, string ContentType, string? Body, string Headers);

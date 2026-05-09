using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using WebhookService.API.Controllers;
using WebhookService.Application.Tokens.Commands.CreateToken;
using WebhookService.Application.Tokens.Commands.DeleteToken;
using WebhookService.Application.Tokens.Commands.ResetCustomResponse;
using WebhookService.Application.Tokens.Commands.SetCustomResponse;
using WebhookService.Application.Tokens.Commands.UpdateToken;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Application.Tokens.Queries.GetTokens;

namespace WebhookService.UnitTests.API.Controllers;

public sealed class TokensControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private TokensController CreateController()
    {
        var controller = new TokensController(_mediator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static TokenDto MakeTokenDto() => new(
        Id: Guid.NewGuid(),
        Token: Guid.NewGuid(),
        WebhookUrl: "https://example.com/webhook/abc",
        Description: "Test token",
        CreatedAt: DateTimeOffset.UtcNow,
        IsActive: true,
        CustomResponse: null);

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsOk_WithTokenList()
    {
        var tokens = new List<TokenDto> { MakeTokenDto() };
        _mediator.Send(Arg.Any<GetTokensQuery>(), Arg.Any<CancellationToken>()).Returns(tokens);

        var result = await CreateController().GetAll(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(tokens);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsOk_WhenTokenFound()
    {
        var dto = MakeTokenDto();
        _mediator.Send(Arg.Any<GetTokenQuery>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateController().GetById(dto.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenTokenMissing()
    {
        _mediator.Send(Arg.Any<GetTokenQuery>(), Arg.Any<CancellationToken>())
            .Returns((TokenDto?)null);

        var result = await CreateController().GetById(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsCreated_WithDto()
    {
        var dto = MakeTokenDto();
        _mediator.Send(Arg.Any<CreateTokenCommand>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateController().Create(new CreateTokenRequest("My token"), CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Create_SendsCorrectDescription()
    {
        CreateTokenCommand? captured = null;
        _mediator.Send(Arg.Do<CreateTokenCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(MakeTokenDto());

        await CreateController().Create(new CreateTokenRequest("New webhook"), CancellationToken.None);

        captured!.Description.Should().Be("New webhook");
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsOk_WhenTokenFound()
    {
        var dto = MakeTokenDto();
        _mediator.Send(Arg.Any<UpdateTokenCommand>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateController().Update(dto.Id, new UpdateTokenRequest("Updated", true), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenTokenMissing()
    {
        _mediator.Send(Arg.Any<UpdateTokenCommand>(), Arg.Any<CancellationToken>())
            .Returns((TokenDto?)null);

        var result = await CreateController().Update(Guid.NewGuid(), new UpdateTokenRequest(null, false), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenTokenFound()
    {
        _mediator.Send(Arg.Any<DeleteTokenCommand>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateController().Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenTokenMissing()
    {
        _mediator.Send(Arg.Any<DeleteTokenCommand>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateController().Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── SetCustomResponse ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetCustomResponse_ReturnsNoContent_WhenTokenFound()
    {
        _mediator.Send(Arg.Any<SetCustomResponseCommand>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateController().SetCustomResponse(
            Guid.NewGuid(),
            new SetCustomResponseRequest(200, "application/json", "{}", "{}"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task SetCustomResponse_ReturnsNotFound_WhenTokenMissing()
    {
        _mediator.Send(Arg.Any<SetCustomResponseCommand>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateController().SetCustomResponse(
            Guid.NewGuid(),
            new SetCustomResponseRequest(200, "application/json", null, "{}"),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── ResetCustomResponse ───────────────────────────────────────────────────

    [Fact]
    public async Task ResetCustomResponse_ReturnsNoContent_WhenTokenFound()
    {
        _mediator.Send(Arg.Any<ResetCustomResponseCommand>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateController().ResetCustomResponse(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task ResetCustomResponse_ReturnsNotFound_WhenTokenMissing()
    {
        _mediator.Send(Arg.Any<ResetCustomResponseCommand>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateController().ResetCustomResponse(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Hookbin.API.Controllers;
using Hookbin.Application.Requests.Commands.ClearRequests;
using Hookbin.Application.Requests.Commands.DeleteRequest;
using Hookbin.Application.Requests.Queries.ExportRequest;
using Hookbin.Application.Requests.Queries.GetRequestById;
using Hookbin.Application.Requests.Queries.GetRequests;

namespace Hookbin.UnitTests.API.Controllers;

public sealed class RequestsControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private RequestsController CreateController()
    {
        var controller = new RequestsController(_mediator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static WebhookRequestDetailDto MakeDetailDto() => new(
        Id: Guid.NewGuid(),
        TokenId: Guid.NewGuid(),
        Method: "POST",
        Path: "/webhook/abc",
        QueryString: null,
        ReceivedAt: DateTimeOffset.UtcNow,
        ContentType: "application/json",
        Headers: "{}",
        Body: "{\"key\":\"val\"}",
        IsBodyBase64: false,
        SizeBytes: 13,
        IpAddress: "127.0.0.1",
        UserAgent: null,
        ProcessingTimeMs: null,
        Note: null,
        ResponseStatusCode: null,
        IpCountry: null);

    // ── GetPaged ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPaged_ReturnsOk_WithPagedResult()
    {
        var paged = new PagedResult<WebhookRequestSummaryDto>([], 0, 1, 20);
        _mediator.Send(Arg.Any<GetRequestsQuery>(), Arg.Any<CancellationToken>()).Returns(paged);

        var result = await CreateController().GetPaged(Guid.NewGuid(), ct: CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(paged);
    }

    [Fact]
    public async Task GetPaged_ForwardsQueryParameters_ToMediator()
    {
        var tokenId = Guid.NewGuid();
        GetRequestsQuery? captured = null;
        _mediator.Send(Arg.Do<GetRequestsQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<WebhookRequestSummaryDto>([], 0, 2, 10));

        await CreateController().GetPaged(tokenId, page: 2, pageSize: 10, search: "test", ct: CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TokenId.Should().Be(tokenId);
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(10);
        captured.Search.Should().Be("test");
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsOk_WhenFound()
    {
        var dto = MakeDetailDto();
        _mediator.Send(Arg.Any<GetRequestByIdQuery>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateController().GetById(dto.TokenId, dto.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        _mediator.Send(Arg.Any<GetRequestByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((WebhookRequestDetailDto?)null);

        var result = await CreateController().GetById(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsFile_WhenFound()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"abc\"}");
        _mediator.Send(Arg.Any<ExportRequestQuery>(), Arg.Any<CancellationToken>()).Returns(bytes);

        var result = await CreateController().Export(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<FileContentResult>()
            .Which.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task Export_ReturnsNotFound_WhenMissing()
    {
        _mediator.Send(Arg.Any<ExportRequestQuery>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var result = await CreateController().Export(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── ClearAll ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAll_ReturnsNoContent()
    {
        var result = await CreateController().ClearAll(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task ClearAll_SendsCommandWithCorrectTokenId()
    {
        var tokenId = Guid.NewGuid();
        ClearRequestsCommand? captured = null;
        _mediator.When(m => m.Send(Arg.Any<ClearRequestsCommand>(), Arg.Any<CancellationToken>()))
                 .Do(info => captured = (ClearRequestsCommand)info[0]);

        await CreateController().ClearAll(tokenId, CancellationToken.None);

        captured!.TokenId.Should().Be(tokenId);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenFound()
    {
        _mediator.Send(Arg.Any<DeleteRequestCommand>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateController().Delete(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMissing()
    {
        _mediator.Send(Arg.Any<DeleteRequestCommand>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateController().Delete(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}

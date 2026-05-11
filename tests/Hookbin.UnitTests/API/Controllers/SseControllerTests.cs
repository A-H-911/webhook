using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Hookbin.API.Controllers;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;
using Hookbin.Domain.Services;

namespace Hookbin.UnitTests.API.Controllers;

public sealed class SseControllerTests
{
    private readonly ISseNotifier _sseNotifier = Substitute.For<ISseNotifier>();
    private readonly IWebhookTokenRepository _tokenRepo = Substitute.For<IWebhookTokenRepository>();

    private SseController CreateController(MemoryStream? body = null)
    {
        var controller = new SseController(_sseNotifier, _tokenRepo);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = body ?? new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static async IAsyncEnumerable<SseEvent> NoEvents([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SseEvent> OneEvent(SseEvent evt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return evt;
        await Task.CompletedTask;
    }

    // ── Token lookup ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_Sets404_WhenTokenNotFound()
    {
        // Arrange
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((WebhookToken?)null);
        var controller = CreateController();

        // Act
        await controller.Subscribe(Guid.NewGuid(), CancellationToken.None);

        // Assert
        controller.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // ── Subscription limit ────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_Sets429_WhenTooManyConnections()
    {
        // Arrange
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid() });
        _sseNotifier.TrySubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var controller = CreateController();

        // Act
        await controller.Subscribe(Guid.NewGuid(), CancellationToken.None);

        // Assert
        controller.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_WritesRetryFrame_OnConnect()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookToken { Id = tokenId, Token = Guid.NewGuid() });
        _sseNotifier.TrySubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sseNotifier.SubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(NoEvents());
        var responseBody = new MemoryStream();
        var controller = CreateController(responseBody);

        // Act
        await controller.Subscribe(tokenId, CancellationToken.None);

        // Assert
        responseBody.Position = 0;
        var written = await new StreamReader(responseBody).ReadToEndAsync();
        written.Should().Contain("retry: 5000");
    }

    [Fact]
    public async Task Subscribe_WritesEventToBody_WhenNotified()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookToken { Id = tokenId, Token = Guid.NewGuid() });
        _sseNotifier.TrySubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sseNotifier.SubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OneEvent(new SseEvent("request", @"{""id"":1}")));
        var responseBody = new MemoryStream();
        var controller = CreateController(responseBody);

        // Act
        await controller.Subscribe(tokenId, CancellationToken.None);

        // Assert
        responseBody.Position = 0;
        var written = await new StreamReader(responseBody).ReadToEndAsync();
        written.Should().Contain("event: request");
        written.Should().Contain(@"data: {""id"":1}");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_CallsUnsubscribe_InFinallyBlock()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookToken { Id = tokenId, Token = Guid.NewGuid() });
        _sseNotifier.TrySubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sseNotifier.SubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(NoEvents());
        var controller = CreateController();

        // Act
        await controller.Subscribe(tokenId, CancellationToken.None);

        // Assert — finally block must always unsubscribe
        await _sseNotifier.Received(1).UnsubscribeAsync(tokenId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_StillUnsubscribes_WhenCancelled()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        _tokenRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookToken { Id = tokenId, Token = Guid.NewGuid() });
        _sseNotifier.TrySubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sseNotifier.SubscribeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(NoEvents());
        var controller = CreateController();

        // Act — cancel before calling Subscribe; WriteAsync throws TaskCanceledException,
        // but the finally block must still run and call UnsubscribeAsync.
        await cts.CancelAsync();
        try { await controller.Subscribe(tokenId, cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — finally block must still call UnsubscribeAsync despite cancellation
        await _sseNotifier.Received(1).UnsubscribeAsync(tokenId, Arg.Any<CancellationToken>());
    }
}

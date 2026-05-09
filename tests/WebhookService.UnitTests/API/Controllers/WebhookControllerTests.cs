using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;
using WebhookService.API.Controllers;
using WebhookService.Application.Caching;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.API.Controllers;

public sealed class WebhookControllerTests
{
    private readonly IWebhookTokenRepository _tokenRepo = Substitute.For<IWebhookTokenRepository>();
    private readonly IRequestQueuePublisher _queuePublisher = Substitute.For<IRequestQueuePublisher>();
    private readonly ITokenCache _tokenCache = Substitute.For<ITokenCache>();
    private readonly ILogger<WebhookController> _logger = Substitute.For<ILogger<WebhookController>>();

    private WebhookController CreateController(string method = "POST", string body = "", string? contentType = null)
    {
        var controller = new WebhookController(_tokenRepo, _queuePublisher, _tokenCache, _logger);

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length > 0 ? bodyBytes.Length : 0;
        httpContext.Request.ContentType = contentType;
        httpContext.Request.Path = "/webhook/test";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        httpContext.Response.Body = new MemoryStream();

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static WebhookToken MakeActiveToken() => new()
    {
        Id = Guid.NewGuid(),
        Token = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true
    };

    private static WebhookToken MakeInactiveToken() => new()
    {
        Id = Guid.NewGuid(),
        Token = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = false
    };

    // ── Token resolution ──────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_Returns404_WhenTokenNotFound()
    {
        // Arrange
        var missingToken = Guid.NewGuid();
        _tokenRepo.GetByTokenIncludingInactiveAsync(missingToken, Arg.Any<CancellationToken>())
            .Returns((WebhookToken?)null);
        var controller = CreateController();

        // Act
        var result = await controller.Receive(missingToken, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Receive_Returns200_WhenActiveTokenExists()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        var result = await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Receive_PublishesRequest_WhenTokenFound()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController(method: "POST", body: "{\"key\":\"value\"}");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert — PublishAsync called exactly once with the correct TokenId
        await _queuePublisher.Received(1).PublishAsync(
            Arg.Is<WebhookRequest>(r => r.TokenId == token.Id),
            Arg.Any<CancellationToken>());
    }

    // ── Inactive token ────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_Returns410_WhenTokenIsInactive()
    {
        // Arrange
        var token = MakeInactiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        var result = await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public async Task Receive_StillPublishesRequest_WhenTokenIsInactive()
    {
        // Arrange — inactive tokens still queue the incoming request for audit purposes
        var token = MakeInactiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await _queuePublisher.Received(1).PublishAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Custom response ────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_ReturnsCustomStatusCode_WhenCustomResponseHasBody()
    {
        // Arrange
        var token = MakeActiveToken();
        token.CustomResponse = new CustomResponse
        {
            StatusCode = 201,
            ContentType = "application/json",
            Body = "{\"status\":\"created\"}",
            Headers = "{}"
        };
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        var result = await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        controller.Response.StatusCode.Should().Be(201);
        result.Should().BeOfType<ContentResult>()
            .Which.Content.Should().Be("{\"status\":\"created\"}");
    }

    [Fact]
    public async Task Receive_ReturnsEmptyResult_WhenCustomResponseHasNullBody()
    {
        // Arrange
        var token = MakeActiveToken();
        token.CustomResponse = new CustomResponse
        {
            StatusCode = 204,
            ContentType = "text/plain",
            Body = null,
            Headers = "{}"
        };
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        var result = await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        controller.Response.StatusCode.Should().Be(204);
        result.Should().BeOfType<EmptyResult>();
    }

    // ── Token cache ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_DoesNotCacheNull_WhenTokenNotFound()
    {
        // Arrange — cache miss, repo miss; null must never be stored in cache
        var missingToken = Guid.NewGuid();
        _tokenCache.GetAsync(missingToken, Arg.Any<CancellationToken>())
            .Returns((WebhookToken?)null);
        _tokenRepo.GetByTokenIncludingInactiveAsync(missingToken, Arg.Any<CancellationToken>())
            .Returns((WebhookToken?)null);
        var controller = CreateController();

        // Act — two requests for a non-existent token
        await controller.Receive(missingToken, CancellationToken.None);
        await controller.Receive(missingToken, CancellationToken.None);

        // Assert — null never cached; repo hit both times
        await _tokenRepo.Received(2)
            .GetByTokenIncludingInactiveAsync(missingToken, Arg.Any<CancellationToken>());
        await _tokenCache.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_UsesCache_WhenTokenFoundInCache()
    {
        // Arrange — cache hit; repo must not be called
        var token = MakeActiveToken();
        _tokenCache.GetAsync(token.Token, Arg.Any<CancellationToken>()).Returns(token);
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await _tokenRepo.DidNotReceive()
            .GetByTokenIncludingInactiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_PopulatesCache_WhenTokenFoundInRepo()
    {
        // Arrange — cache miss, repo hit; token should be stored in cache
        var token = MakeActiveToken();
        _tokenCache.GetAsync(token.Token, Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>()).Returns(token);
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await _tokenCache.Received(1).SetAsync(token.Token, token, Arg.Any<CancellationToken>());
    }

    // ── Request field capture ─────────────────────────────────────────────────

    [Fact]
    public async Task Receive_CapturesHttpMethod_InPublishedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController(method: "DELETE");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be("DELETE");
    }

    [Fact]
    public async Task Receive_CapturesIpAddress_InPublishedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.IpAddress.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task Receive_SetsNullBody_WhenContentLengthIsZero()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController(body: "");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Body.Should().BeNull();
    }

    [Fact]
    public async Task Receive_CapturesBody_WhenBodyIsPresent()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController(body: "{\"event\":\"push\"}", contentType: "application/json");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Body.Should().Be("{\"event\":\"push\"}");
    }

    [Fact]
    public async Task Receive_SetsCorrectTokenId_InPublishedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.TokenId.Should().Be(token.Id);
    }

    [Fact]
    public async Task Receive_SetsNewGuidId_InPublishedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _queuePublisher.PublishAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert — each request gets a fresh unique ID
        captured.Should().NotBeNull();
        captured!.Id.Should().NotBe(Guid.Empty);
    }

    // ── ReadBodyAsync IOException path ────────────────────────────────────────

    [Fact]
    public async Task Receive_ThrowsBadHttpRequestException_WhenBodyStreamThrowsIOException()
    {
        // Arrange — use a stream that throws IOException on read to exercise the catch block
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);

        var controller = new WebhookController(_tokenRepo, _queuePublisher, _tokenCache, _logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Body = new ThrowingStream();
        httpContext.Request.ContentLength = 10; // non-zero so ReadBodyAsync is entered
        httpContext.Request.Path = "/webhook/test";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var act = () => controller.Receive(token.Token, CancellationToken.None);

        // Assert — IOException is translated to BadHttpRequestException by ReadBodyAsync
        await act.Should().ThrowAsync<BadHttpRequestException>()
            .WithMessage("*Could not read request body*");
    }

    // ── SseEvent domain record ────────────────────────────────────────────────

    [Fact]
    public void SseEvent_StoresEventNameAndData()
    {
        // Arrange & Act
        var evt = new SseEvent("request", "{\"id\":\"abc\"}");

        // Assert
        evt.EventName.Should().Be("request");
        evt.Data.Should().Be("{\"id\":\"abc\"}");
    }

    [Fact]
    public void SseEvent_EqualityByValue()
    {
        // Arrange
        var a = new SseEvent("request", "data");
        var b = new SseEvent("request", "data");

        // Assert — record equality is structural
        a.Should().Be(b);
    }
}

/// <summary>
/// A stream that throws <see cref="IOException"/> on every read,
/// used to exercise the catch block in WebhookController.ReadBodyAsync.
/// </summary>
file sealed class ThrowingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new IOException("Simulated read failure");

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new IOException("Simulated read failure");

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException("Simulated read failure");

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

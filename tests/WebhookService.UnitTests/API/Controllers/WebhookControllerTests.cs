using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text;
using WebhookService.API.Controllers;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.API.Controllers;

public sealed class WebhookControllerTests
{
    private readonly IWebhookTokenRepository _tokenRepo = Substitute.For<IWebhookTokenRepository>();
    private readonly IWebhookRequestRepository _requestRepo = Substitute.For<IWebhookRequestRepository>();
    private readonly ISseNotifier _sseNotifier = Substitute.For<ISseNotifier>();
    private readonly ILogger<WebhookController> _logger = Substitute.For<ILogger<WebhookController>>();

    /// <summary>
    /// Each test gets a fresh MemoryCache so cache state does not leak between tests.
    /// </summary>
    private WebhookController CreateController(string method = "POST", string body = "", string? contentType = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new WebhookController(_tokenRepo, _requestRepo, _sseNotifier, cache, _logger);

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
    public async Task Receive_PersistsRequest_WhenTokenFound()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController(method: "POST", body: "{\"key\":\"value\"}");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert — AddAsync called exactly once with the correct TokenId
        await _requestRepo.Received(1).AddAsync(
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
    public async Task Receive_StillPersistsRequest_WhenTokenIsInactive()
    {
        // Arrange — inactive tokens still record the incoming request for audit purposes
        var token = MakeInactiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await _requestRepo.Received(1).AddAsync(Arg.Any<WebhookRequest>(), Arg.Any<CancellationToken>());
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
        // Arrange — each controller gets its own shared cache via field
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller1 = new WebhookController(_tokenRepo, _requestRepo, _sseNotifier, cache, _logger);
        var controller2 = new WebhookController(_tokenRepo, _requestRepo, _sseNotifier, cache, _logger);

        var missingToken = Guid.NewGuid();
        _tokenRepo.GetByTokenIncludingInactiveAsync(missingToken, Arg.Any<CancellationToken>())
            .Returns((WebhookToken?)null);

        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Body = new MemoryStream();
        httpContext1.Request.ContentLength = 0;
        httpContext1.Response.Body = new MemoryStream();
        httpContext1.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        controller1.ControllerContext = new ControllerContext { HttpContext = httpContext1 };

        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Body = new MemoryStream();
        httpContext2.Request.ContentLength = 0;
        httpContext2.Response.Body = new MemoryStream();
        httpContext2.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        controller2.ControllerContext = new ControllerContext { HttpContext = httpContext2 };

        // Act — two requests for a non-existent token
        await controller1.Receive(missingToken, CancellationToken.None);
        await controller2.Receive(missingToken, CancellationToken.None);

        // Assert — null is never cached; repo called both times
        await _tokenRepo.Received(2)
            .GetByTokenIncludingInactiveAsync(missingToken, Arg.Any<CancellationToken>());
    }

    // ── SSE notification ──────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_NotifiesSse_WhenActiveTokenAndRequestPersisted()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await _sseNotifier.Received(1).NotifyAsync(
            token.Id,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_DoesNotThrow_WhenSseNotifierFails()
    {
        // Arrange — SSE failure must be swallowed so the HTTP response still completes
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        _sseNotifier.NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("channel full"));
        var controller = CreateController();

        // Act
        var act = () => controller.Receive(token.Token, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Receive_Returns200_EvenWhenSseNotifierFails()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        _sseNotifier.NotifyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("channel full"));
        var controller = CreateController();

        // Act
        var result = await controller.Receive(token.Token, CancellationToken.None);

        // Assert — SSE failure is non-fatal; caller still gets 200 OK
        result.Should().BeOfType<OkObjectResult>();
    }

    // ── Request field capture ─────────────────────────────────────────────────

    [Fact]
    public async Task Receive_CapturesHttpMethod_InPersistedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController(method: "DELETE");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be("DELETE");
    }

    [Fact]
    public async Task Receive_CapturesIpAddress_InPersistedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
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
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        // Empty body -> ContentLength == 0
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
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController(body: "{\"event\":\"push\"}", contentType: "application/json");

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Body.Should().Be("{\"event\":\"push\"}");
    }

    [Fact]
    public async Task Receive_SetsCorrectTokenId_InPersistedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.TokenId.Should().Be(token.Id);
    }

    [Fact]
    public async Task Receive_SetsNewGuidId_InPersistedRequest()
    {
        // Arrange
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);
        WebhookRequest? captured = null;
        await _requestRepo.AddAsync(Arg.Do<WebhookRequest>(r => captured = r), Arg.Any<CancellationToken>());
        var controller = CreateController();

        // Act
        await controller.Receive(token.Token, CancellationToken.None);

        // Assert — each request gets a fresh unique ID
        captured.Should().NotBeNull();
        captured!.Id.Should().NotBe(Guid.Empty);
    }

    // ── SseEvent domain record ────────────────────────────────────────────────

    // ── ReadBodyAsync IOException path ────────────────────────────────────────

    [Fact]
    public async Task Receive_ThrowsBadHttpRequestException_WhenBodyStreamThrowsIOException()
    {
        // Arrange — use a stream that throws IOException on read to exercise the catch block
        var token = MakeActiveToken();
        _tokenRepo.GetByTokenIncludingInactiveAsync(token.Token, Arg.Any<CancellationToken>())
            .Returns(token);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new WebhookController(_tokenRepo, _requestRepo, _sseNotifier, cache, _logger);

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

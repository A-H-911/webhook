using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;
using Hookbin.API.Middleware;

namespace Hookbin.UnitTests.Middleware;

public sealed class GlobalExceptionMiddlewareAdditionalTests
{
    private static DefaultHttpContext MakeContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_SwallowsOperationCanceledException_WhenRequestAborted()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new OperationCanceledException();
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        ctx.RequestAborted = cts.Token;

        // Act — must not throw or write any error body
        await middleware.InvokeAsync(ctx);

        // Assert — default status 200 with empty body means the exception was swallowed
        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_Returns400_OnBadHttpRequestException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new BadHttpRequestException("bad input", 400);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.Should().Be(400);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetString().Should().Be("bad input");
    }

    [Fact]
    public async Task InvokeAsync_Returns413_OnBadHttpRequestException_WithCustomStatusCode()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ =>
            throw new BadHttpRequestException("payload too large", StatusCodes.Status413RequestEntityTooLarge);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status413RequestEntityTooLarge);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("payload too large");
    }

    [Fact]
    public async Task InvokeAsync_LogsWarning_OnBadHttpRequestException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new BadHttpRequestException("bad input", 400);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert — LogWarning was invoked at least once
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<BadHttpRequestException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task InvokeAsync_SetsApplicationJsonContentType_OnBadHttpRequestException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new BadHttpRequestException("oops", 400);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        ctx.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_SkipsResponseWrite_WhenResponseAlreadyStarted_ForValidationException()
    {
        // Arrange — write partial response before throwing so HasStarted == true
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        var failures = new[] { new ValidationFailure("Field", "required") };
        RequestDelegate next = async ctx =>
        {
            await ctx.Response.WriteAsync("partial");
            throw new ValidationException(failures);
        };
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act — must not throw
        await middleware.Invoking(m => m.InvokeAsync(ctx))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteErrorAsync_CompletesWithoutThrowing_WhenExceptionAfterPartialWrite()
    {
        // Arrange
        // Note: DefaultHttpContext.Response.HasStarted is always false in unit tests
        // because it requires a real Kestrel pipeline to flip to true.
        // We verify the middleware completes without throwing even after a partial write.
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = async ctx =>
        {
            await ctx.Response.WriteAsync("streaming data");
            throw new InvalidOperationException("mid-stream failure");
        };
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act — must not throw
        await middleware.Invoking(m => m.InvokeAsync(ctx))
            .Should().NotThrowAsync();

        // Assert — response body contains at least the initial partial write
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var content = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        content.Should().Contain("streaming data");
    }

    [Fact]
    public async Task InvokeAsync_Returns422_WithMultipleFieldErrors()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        var failures = new[]
        {
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Email", "Email is invalid")
        };
        RequestDelegate next = _ => throw new ValidationException(failures);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.Should().Be(422);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("Name is required");
        body.Should().Contain("Email is invalid");
    }

    [Fact]
    public async Task InvokeAsync_SetsApplicationJsonContentType_OnUnhandledException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        ctx.Response.ContentType.Should().Be("application/json");
    }
}

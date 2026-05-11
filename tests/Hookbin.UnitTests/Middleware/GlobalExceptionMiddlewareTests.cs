using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;
using Hookbin.API.Middleware;

namespace Hookbin.UnitTests.Middleware;

public sealed class GlobalExceptionMiddlewareTests
{
    private static DefaultHttpContext MakeContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_Returns422_OnValidationException()
    {
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        var failures = new[] { new ValidationFailure("Field", "is required") };
        RequestDelegate next = _ => throw new ValidationException(failures);
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("is required");
    }

    [Fact]
    public async Task InvokeAsync_Returns500_OnUnhandledException()
    {
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = _ => throw new InvalidOperationException("unexpected");
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("unexpected error");
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenNoException()
    {
        var logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        RequestDelegate next = ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; };
        var middleware = new GlobalExceptionMiddleware(next, logger);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }
}
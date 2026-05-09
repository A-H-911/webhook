using System.Net;
using System.Text.Json;
using FluentValidation;
using StackExchange.Redis;

namespace WebhookService.API.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — normal for SSE and long-running streams; not an error.
        }
        catch (ValidationException ex)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                context.Response.ContentType = "application/json";
                var errors = ex.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage });
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { errors }));
            }
        }
        catch (BadHttpRequestException ex)
        {
            logger.LogWarning(ex, "Bad HTTP request for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogError(ex, "Redis unavailable for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.Headers.RetryAfter = "5";
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                "Service temporarily unavailable. Please retry shortly.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, (int)HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
    }
}

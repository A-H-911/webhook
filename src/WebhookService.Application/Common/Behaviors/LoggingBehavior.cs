using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace WebhookService.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next(ct);
            _logger.LogInformation("{Handler} succeeded in {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (FluentValidation.ValidationException)
        {
            // Validation failures are expected client errors — log at Warning, not Error
            _logger.LogWarning("{Handler} rejected due to validation failure in {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Handler} failed after {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

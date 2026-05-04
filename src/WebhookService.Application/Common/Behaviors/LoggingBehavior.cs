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
            return await next(ct);
        }
        finally
        {
            _logger.LogInformation("{Handler} completed in {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookService.Domain.Repositories;

namespace WebhookService.Infrastructure.BackgroundServices;

public sealed class RetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RetentionCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var retentionDays = configuration.GetValue<int>("Webhook:RetentionDays", 7);
                if (retentionDays <= 0) continue;

                await using var scope = scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWebhookRequestRepository>();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
                var count = await repo.DeleteOlderThanAsync(cutoff, stoppingToken);

                logger.LogInformation(
                    "Retention cleanup deleted {Count} requests older than {Cutoff:O}", count, cutoff);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention cleanup failed — will retry on next tick");
            }
        }
    }
}

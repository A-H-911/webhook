using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure.BackgroundServices;
using WebhookService.Infrastructure.Persistence;
using WebhookService.Infrastructure.Persistence.Repositories;
using WebhookService.Infrastructure.Sse;

namespace WebhookService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseSqlServer(
                configuration.GetConnectionString("WebhookDb"),
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorNumbersToAdd: null)));

        services.AddScoped<IWebhookTokenRepository, WebhookTokenRepository>();
        services.AddScoped<IWebhookRequestRepository, WebhookRequestRepository>();

        services.AddSingleton<ISseNotifier, SseNotifier>();

        services.AddHostedService<RetentionCleanupService>();

        return services;
    }
}

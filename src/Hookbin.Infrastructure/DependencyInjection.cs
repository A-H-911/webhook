using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Hookbin.Application.Caching;
using Hookbin.Application.GeoIp;
using Hookbin.Domain.Repositories;
using Hookbin.Domain.Services;
using Hookbin.Infrastructure.BackgroundServices;
using Hookbin.Infrastructure.GeoIp;
using Hookbin.Infrastructure.Persistence;
using Hookbin.Infrastructure.Persistence.Repositories;
using Hookbin.Infrastructure.Redis;
using Hookbin.Infrastructure.Sse;

namespace Hookbin.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Core infrastructure shared by every process: DB context, repositories, Redis multiplexer.
    /// </summary>
    public static IServiceCollection AddCoreInfrastructure(
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

        // Read inside factory so test-factory ConfigureAppConfiguration overrides apply at resolution time.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var redisConnection = cfg.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        return services;
    }

    /// <summary>
    /// API-only infrastructure: stream publisher, token cache, SSE notifier, SSE bridge.
    /// </summary>
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IGeoIpService, MaxMindGeoIpService>();
        services.AddSingleton<IRequestQueuePublisher, RedisStreamPublisher>();
        services.AddSingleton<ITokenCache, RedisTokenCache>();
        services.AddSingleton<ISseNotifier, SseNotifier>();
        services.AddHostedService<RedisSseBridgeService>();
        return services;
    }

    /// <summary>
    /// StreamWorker-only infrastructure: Redis stream consumer background service.
    /// </summary>
    public static IServiceCollection AddStreamWorkerInfrastructure(this IServiceCollection services)
    {
        services.AddHostedService<RedisStreamConsumerService>();
        return services;
    }

    /// <summary>
    /// JobsWorker-only infrastructure: retention cleanup background service.
    /// </summary>
    public static IServiceCollection AddJobsWorkerInfrastructure(this IServiceCollection services)
    {
        services.AddHostedService<RetentionCleanupService>();
        return services;
    }
}

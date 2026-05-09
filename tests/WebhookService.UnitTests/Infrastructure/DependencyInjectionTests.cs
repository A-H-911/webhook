using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using StackExchange.Redis;
using WebhookService.Application.Caching;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure;
using WebhookService.Infrastructure.BackgroundServices;
using WebhookService.Infrastructure.Redis;
using WebhookService.Infrastructure.Sse;

namespace WebhookService.UnitTests.Infrastructure;

/// <summary>
/// Phase 1a RED tests for the DI split.
/// Compile-time RED: AddCoreInfrastructure, AddApiInfrastructure,
/// AddStreamWorkerInfrastructure, AddJobsWorkerInfrastructure do not exist yet.
/// </summary>
public sealed class DependencyInjectionTests
{
    private static IConfiguration MakeConfig()
    {
        var config = Substitute.For<IConfiguration>();
        var section = Substitute.For<IConfigurationSection>();
        config.GetSection(Arg.Any<string>()).Returns(section);
        config.GetConnectionString("WebhookDb").Returns("Server=test;Database=test;TrustServerCertificate=True");
        config.GetConnectionString("Redis").Returns("localhost:6379,abortConnect=false");
        return config;
    }

    // ── AddCoreInfrastructure ─────────────────────────────────────────────────

    [Fact]
    public void AddCoreInfrastructure_RegistersDbContextAndRepositoriesAndConnectionMultiplexer_Only()
    {
        var services = new ServiceCollection();
        // RED: AddCoreInfrastructure does not exist yet — compile error
        services.AddCoreInfrastructure(MakeConfig());

        // Registered by core
        services.Should().Contain(d => d.ServiceType == typeof(IWebhookTokenRepository));
        services.Should().Contain(d => d.ServiceType == typeof(IWebhookRequestRepository));
        services.Should().Contain(d => d.ServiceType == typeof(IConnectionMultiplexer));

        // API-specific services must NOT be registered by core alone
        services.Should().NotContain(d => d.ServiceType == typeof(IRequestQueuePublisher));
        services.Should().NotContain(d => d.ServiceType == typeof(ITokenCache));
        services.Should().NotContain(d => d.ServiceType == typeof(ISseNotifier));

        // No background services
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    // ── AddApiInfrastructure ──────────────────────────────────────────────────

    [Fact]
    public void AddApiInfrastructure_RegistersPublisherTokenCacheSseNotifier_AndRedisSseBridgeHostedService()
    {
        var services = new ServiceCollection();
        // RED: AddApiInfrastructure does not exist yet — compile error
        services.AddApiInfrastructure();

        services.Should().Contain(d => d.ServiceType == typeof(IRequestQueuePublisher));
        services.Should().Contain(d => d.ServiceType == typeof(ITokenCache));
        services.Should().Contain(d => d.ServiceType == typeof(ISseNotifier));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisSseBridgeService));

        // Must NOT register worker-only services
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisStreamConsumerService));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RetentionCleanupService));
    }

    // ── AddStreamWorkerInfrastructure ─────────────────────────────────────────

    [Fact]
    public void AddStreamWorkerInfrastructure_RegistersOnlyRedisStreamConsumerServiceAsHostedService()
    {
        var services = new ServiceCollection();
        // RED: AddStreamWorkerInfrastructure does not exist yet — compile error
        services.AddStreamWorkerInfrastructure();

        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisStreamConsumerService));

        // Only ONE hosted service registered
        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Should().HaveCount(1);

        // Must NOT register ISseNotifier (in-process channel belongs to API only)
        services.Should().NotContain(d => d.ServiceType == typeof(ISseNotifier));
    }

    // ── AddJobsWorkerInfrastructure ───────────────────────────────────────────

    [Fact]
    public void AddJobsWorkerInfrastructure_RegistersOnlyRetentionCleanupServiceAsHostedService()
    {
        var services = new ServiceCollection();
        // RED: AddJobsWorkerInfrastructure does not exist yet — compile error
        services.AddJobsWorkerInfrastructure();

        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RetentionCleanupService));

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Should().HaveCount(1);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisStreamConsumerService));
    }

    // ── Obsolete shim removal gate ────────────────────────────────────────────

    [Fact]
    public void AddInfrastructure_IsRemoved()
    {
        // RED: fails while the [Obsolete] shim still exists; GREEN after Phase 4b removes it.
        var method = typeof(DependencyInjection)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "AddInfrastructure");

        method.Should().BeNull(
            "AddInfrastructure shim must be removed; callers must use focused extensions");
    }

}

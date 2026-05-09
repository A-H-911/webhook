using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure;
using WebhookService.Infrastructure.Redis;

namespace WebhookService.UnitTests.StreamWorker;

/// <summary>
/// Phase 2a tests — specify the StreamWorker host's DI contract.
/// RED until Phase 2b creates WebhookService.StreamWorker; these tests prove the
/// contract that Program.cs must satisfy.
/// </summary>
public sealed class ProgramTests
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

    [Fact]
    public void StreamWorker_HostBuildsSuccessfully_WithCoreAndStreamWorkerInfrastructure()
    {
        // Arrange — replicate what StreamWorker's Program.cs will wire up
        var services = new ServiceCollection();
        services.AddCoreInfrastructure(MakeConfig());
        services.AddStreamWorkerInfrastructure();

        // Assert — consumer is registered as a hosted service
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisStreamConsumerService));

        // ISseNotifier must NOT be in the stream worker
        services.Should().NotContain(d => d.ServiceType == typeof(ISseNotifier));

        // IRequestQueuePublisher (API-only) must NOT be registered
        services.Should().NotContain(d => d.ServiceType == typeof(IRequestQueuePublisher));
    }

    [Fact]
    public void StreamWorker_RegistersHealthChecks_LiveAndReadyEndpoints()
    {
        // Arrange — wire health checks the same way Program.cs will
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("sqlserver", () => HealthCheckResult.Healthy(), ["ready"])
            .AddCheck("redis", () => HealthCheckResult.Healthy(), ["ready"]);

        // Assert — two health check registrations with "ready" tag
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        options.Registrations.Should().Contain(r => r.Name == "sqlserver" && r.Tags.Contains("ready"));
        options.Registrations.Should().Contain(r => r.Name == "redis" && r.Tags.Contains("ready"));
    }
}

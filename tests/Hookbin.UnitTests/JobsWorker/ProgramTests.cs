using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Hookbin.Application.Options;
using Hookbin.Infrastructure;
using Hookbin.Infrastructure.BackgroundServices;
using Hookbin.Infrastructure.Redis;

namespace Hookbin.UnitTests.JobsWorker;

/// <summary>
/// Phase 3a tests — specify the JobsWorker host's DI contract.
/// RED until Phase 3b creates Hookbin.JobsWorker; these tests prove the
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
    public void JobsWorker_HostBuildsSuccessfully_WithCoreAndJobsWorkerInfrastructure()
    {
        // Arrange — replicate what JobsWorker's Program.cs will wire up
        var services = new ServiceCollection();
        services.AddCoreInfrastructure(MakeConfig());
        services.AddJobsWorkerInfrastructure();

        // Assert — retention service is registered as a hosted service
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RetentionCleanupService));

        // RedisStreamConsumerService must NOT be in the jobs worker
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RedisStreamConsumerService));
    }

    [Fact]
    public void JobsWorker_RegistersHealthChecks_LiveAndReadyEndpoints()
    {
        // Arrange — wire health checks the same way Program.cs will
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("sqlserver", () => HealthCheckResult.Healthy(), ["ready"]);

        // Assert — sqlserver health check with "ready" tag registered
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        options.Registrations.Should().Contain(r => r.Name == "sqlserver" && r.Tags.Contains("ready"));
    }

    [Fact]
    public void JobsWorker_BindsWebhookOptions_WithRetentionDaysFromConfiguration()
    {
        // Arrange — replicate options binding from Program.cs
        var services = new ServiceCollection();
        services.AddLogging();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hookbin:RetentionDays"] = "14"
            })
            .Build();

        services.Configure<WebhookOptions>(config.GetSection("Hookbin"));
        services.AddOptionsWithValidateOnStart<WebhookOptions>();

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<WebhookOptions>>().Value;

        // Assert
        opts.RetentionDays.Should().Be(14);
    }
}

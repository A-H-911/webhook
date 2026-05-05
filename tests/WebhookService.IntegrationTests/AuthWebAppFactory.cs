using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Testcontainers.MsSql;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure.Persistence;

namespace WebhookService.IntegrationTests;

/// <summary>
/// Test factory that uses REAL cookie authentication — no bypass.
/// Used by AuthApiTests to verify actual login/logout/protected-endpoint behavior.
/// </summary>
public sealed class AuthWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _db = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public const string TestUsername = "admin";
    public const string TestPassword = "test-P@ssw0rd-123";

    // Low cost factor (4) for test speed; still a valid BCrypt hash format
    public static readonly string TestPasswordHash =
        BCrypt.Net.BCrypt.HashPassword(TestPassword, workFactor: 4);

    public async Task InitializeAsync() => await _db.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebhookDb"] = _db.GetConnectionString(),
                ["Webhook:BaseUrl"] = "https://test.example.com",
                ["Webhook:RetentionDays"] = "7",
                ["Webhook:MaxRequestSizeMb"] = "5",
                ["Cors:AllowedOrigins"] = "",
                ["Auth:Username"] = TestUsername,
                ["Auth:PasswordHash"] = TestPasswordHash,
                ["Auth:SessionHours"] = "8",
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor is not null) services.Remove(dbDescriptor);

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseSqlServer(_db.GetConnectionString()));

            var sseDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISseNotifier));
            if (sseDescriptor is not null) services.Remove(sseDescriptor);

            services.AddSingleton<ISseNotifier, TestNullSseNotifier>();

            // Override rate limiter: integration tests share 127.0.0.1 and would exhaust
            // the 5/min fixed window immediately. PostConfigure replaces the "login" policy
            // with an effectively unlimited one so tests aren't rate-throttled.
            // PolicyMap is internal, so reflection is required to remove the existing key first.
            services.PostConfigure<RateLimiterOptions>(opts =>
            {
                var policyMap = typeof(RateLimiterOptions)
                    .GetProperty("PolicyMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(opts) as System.Collections.IDictionary;
                policyMap?.Remove("login");

                opts.AddFixedWindowLimiter("login", policy =>
                {
                    policy.PermitLimit = int.MaxValue;
                    policy.Window = TimeSpan.FromMinutes(1);
                    policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    policy.QueueLimit = 0;
                });
            });

            // Production cookie authentication is preserved — no TestAuthHandler override.
        });
    }

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

}

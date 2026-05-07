using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;
using WebhookService.Domain.Services;
using WebhookService.Infrastructure.Persistence;

namespace WebhookService.IntegrationTests;

public sealed class WebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _db = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
    }

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
                ["Auth:Username"] = "testuser",
                ["Auth:PasswordHash"] = BCrypt.Net.BCrypt.HashPassword("testpass", workFactor: 4),
                ["Auth:SessionHours"] = "8"
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseSqlServer(_db.GetConnectionString()));

            var sseDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISseNotifier));
            if (sseDescriptor is not null)
                services.Remove(sseDescriptor);

            services.AddSingleton<ISseNotifier, TestNullSseNotifier>();

            services.AddAuthentication(defaultScheme: "Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            services.RemoveAll<IAntiforgery>();
            services.AddSingleton<IAntiforgery, NoOpAntiforgery>();
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
    }

}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
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
                ["Cors:AllowedOrigins"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseSqlServer(_db.GetConnectionString()));
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
    }
}

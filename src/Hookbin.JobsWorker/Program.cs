using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Polly;
using Serilog;
using Hookbin.Application.Options;
using Hookbin.Infrastructure;
using Hookbin.Infrastructure.Persistence;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(o => o.ServiceName = "Hookbin.JobsWorker");
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddCoreInfrastructure(builder.Configuration);
builder.Services.AddJobsWorkerInfrastructure();

builder.Services
    .Configure<WebhookOptions>(builder.Configuration.GetSection("Hookbin"))
    .AddOptionsWithValidateOnStart<WebhookOptions>();

builder.Services
    .AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("WebhookDb")!,
        name: "sqlserver",
        tags: ["ready"]);

var app = builder.Build();

await WaitForDbReadyAsync(app.Services, app.Logger);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
}).AllowAnonymous();

await app.RunAsync();

static async Task WaitForDbReadyAsync(IServiceProvider sp, ILogger log)
{
    await Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(
            retryCount: 30,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Min(attempt * 2, 30)),
            onRetry: (ex, ts, attempt, _) =>
                log.LogWarning(ex, "DB not ready (attempt {Attempt}); retrying in {Delay}", attempt, ts))
        .ExecuteAsync(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException("DB not reachable");
        });
}

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Serilog;
using WebhookService.API.Middleware;
using WebhookService.API.Options;
using WebhookService.Application;
using WebhookService.Infrastructure;
using WebhookService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Options — validated at startup; app refuses to start if BaseUrl is missing
builder.Services
    .Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"))
    .AddSingleton<IValidateOptions<WebhookOptions>, WebhookOptionsValidator>()
    .AddOptionsWithValidateOnStart<WebhookOptions>();

var webhookOptions = builder.Configuration.GetSection("Webhook").Get<WebhookOptions>()
    ?? new WebhookOptions();

// Kestrel request size limit
builder.WebHost.ConfigureKestrel(opts =>
    opts.Limits.MaxRequestBodySize = webhookOptions.MaxRequestSizeMb * 1024L * 1024L);

// Application + Infrastructure layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// CORS — entire block skipped if AllowedOrigins is empty/whitespace
var rawOrigins = builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty;
var origins = rawOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Length > 0)
{
    builder.Services.AddCors(opts => opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader()));
}

builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("WebhookDb")!,
        name: "sqlserver",
        tags: ["ready"]);

var app = builder.Build();

// UseForwardedHeaders MUST come before routing/CORS so real client IP is resolved
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost
});

app.UseMiddleware<GlobalExceptionMiddleware>();

if (origins.Length > 0)
    app.UseCors();

if (app.Environment.IsDevelopment())
    app.UseSwagger().UseSwaggerUI();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

// EF migration with retry — SQL Server container may not be ready immediately
await Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(attempt * 2))
    .ExecuteAsync(async () =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    });

app.Run();

public partial class Program { }

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Serilog;
using WebhookService.API.Middleware;
using WebhookService.API.Services;
using WebhookService.Application.Options;
using WebhookService.Application;
using WebhookService.Infrastructure;
using WebhookService.Infrastructure.Persistence;
using AuthOptions = WebhookService.API.Options.AuthOptions;
using AuthOptionsValidator = WebhookService.API.Options.AuthOptionsValidator;

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

// Auth options — startup fails if Auth:Username or Auth:PasswordHash are missing/invalid
builder.Services
    .Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName))
    .AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>()
    .AddOptionsWithValidateOnStart<AuthOptions>();

// Session revocation — in-memory store; single admin, so no distributed store needed
builder.Services.AddSingleton<SessionRevocationStore>();

// Cookie authentication — cookies are the only browser-native mechanism that works with SSE
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();

// Post-configure cookie options using IOptions<AuthOptions> so SessionHours is resolved
// from the validated options object rather than from a raw eager config read.
builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((cookie, auth) =>
    {
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.SlidingExpiration = true;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(auth.Value.SessionHours);
        cookie.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookie.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        cookie.Events.OnValidatePrincipal = ctx =>
        {
            var sid = ctx.Principal?.FindFirstValue("sid");
            var store = ctx.HttpContext.RequestServices.GetRequiredService<SessionRevocationStore>();
            if (sid is not null && store.IsRevoked(sid))
            {
                ctx.RejectPrincipal();
                ctx.ShouldRenew = false;
            }
            return Task.CompletedTask;
        };
    });

// Global fallback policy — all endpoints require auth unless explicitly [AllowAnonymous]
builder.Services.AddAuthorization(opts =>
{
    opts.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Rate limiting — login brute-force + per-token receiver flood protection
builder.Services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("login", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 0;
    });

    // Per-token token-bucket: default 250 req/s. Override via WEBHOOK__ReceiverRateLimitPerSecond.
    var rateLimit = webhookOptions.ReceiverRateLimitPerSecond;
    opts.AddPolicy("webhook-receiver", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            httpContext.GetRouteValue("token")?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = rateLimit,
                TokensPerPeriod = rateLimit,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CSRF protection — Angular reads XSRF-TOKEN cookie and echoes as X-XSRF-TOKEN header
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "XSRF-TOKEN";
    o.Cookie.HttpOnly = false; // must be JS-readable
    o.Cookie.SameSite = SameSiteMode.Strict;
});

// Application + Infrastructure layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// CORS — entire block skipped if AllowedOrigins is empty/whitespace
var rawOrigins = builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty;
var origins = rawOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Length > 0)
{
    builder.Services.AddCors(opts => opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
}

builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddSqlServer(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("WebhookDb")!,
        name: "sqlserver",
        tags: ["ready"]);

var app = builder.Build();

// L4: Fail fast on misconfigured CORS origins
foreach (var origin in origins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out _))
        throw new InvalidOperationException($"Cors:AllowedOrigins contains invalid URI: '{origin}'");
}

// UseForwardedHeaders MUST come before routing/CORS so real client IP is resolved.
// Restrict to private RFC-1918 ranges (Docker bridge networks, LAN proxies).
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
app.UseForwardedHeaders(forwardedOptions);

app.UseMiddleware<GlobalExceptionMiddleware>();

if (origins.Length > 0)
    app.UseCors();

app.UseRateLimiter();

app.UseAuthentication();

// Emit XSRF-TOKEN cookie for authenticated sessions; Angular HttpClientXsrfModule reads it.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var af = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = af.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
            new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict });
    }
    await next();
});

app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.UseSwagger().UseSwaggerUI();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
}).AllowAnonymous();

// EF migration with retry — SQL Server container may not be ready immediately
await Policy
    .Handle<Exception>(ex => ex is not OperationCanceledException)
    .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(attempt * 2))
    .ExecuteAsync(async () =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    });

app.Run();

public partial class Program { }

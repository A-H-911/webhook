using System.Reflection;
using System.Text.Json.Serialization;
using Hookbin.Domain.Entities;
using Hookbin.Application.Tokens.Commands.CreateToken;
using Microsoft.AspNetCore.Authorization;

namespace Hookbin.ArchitectureTests.Conventions;

/// <summary>
/// Codifies the zero-trust audit invariants as cheap, ever-running architecture rules.
/// Each rule guards a specific bug class surfaced by the MCP walkthrough or the DANGER ZONE
/// section in CLAUDE.md. Rules use reflection where possible, fall back to project-source
/// file scans where ArchUnitNET can't express the predicate.
/// </summary>
public sealed class ZeroTrustInvariantsTests
{
    private static string RepoRoot
    {
        get
        {
            // Walk up from bin/Debug/netX.Y/ to repo root (where Hookbin.slnx lives).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hookbin.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root from AppContext.BaseDirectory");
        }
    }

    private static string SrcFile(params string[] segments) =>
        Path.Combine(RepoRoot, Path.Combine(new[] { "src" }.Concat(segments).ToArray()));

    // ── Rule 1: Repository read methods call .AsNoTracking() ─────────────────────

    [Fact]
    public void WebhookTokenRepository_ReadMethods_AllUse_AsNoTracking()
    {
        var path = SrcFile("Hookbin.Infrastructure", "Persistence", "Repositories", "WebhookTokenRepository.cs");
        var text = File.ReadAllText(path);

        var lines = text.Split('\n');
        var queryLines = lines
            .Where(l => l.Contains("_db.WebhookTokens") && !l.Contains("AddAsync") && !l.Contains("Update("))
            // Only materializing reads need AsNoTracking. CountAsync/AnyAsync are aggregates and don't materialize entities.
            .Where(l => l.Contains("FirstOrDefaultAsync") || l.Contains("ToListAsync")
                     || l.Contains("SingleOrDefaultAsync") || l.Contains("ToArrayAsync"))
            .ToList();

        queryLines.Should().NotBeEmpty("WebhookTokenRepository must have at least one read query");
        foreach (var line in queryLines)
        {
            line.Should().Contain(".AsNoTracking()",
                "every read query in WebhookTokenRepository must use AsNoTracking — DANGER ZONE invariant. Offending line: {0}",
                line.Trim());
        }
    }

    [Fact]
    public void WebhookRequestRepository_ReadMethods_AllUse_AsNoTracking()
    {
        var path = SrcFile("Hookbin.Infrastructure", "Persistence", "Repositories", "WebhookRequestRepository.cs");
        var text = File.ReadAllText(path);

        var lines = text.Split('\n');
        var queryLines = lines
            .Where(l => l.Contains("_db.WebhookRequests") && !l.Contains("AddAsync") && !l.Contains("Update("))
            // Only materializing reads need AsNoTracking. CountAsync/AnyAsync are aggregates and don't materialize entities.
            .Where(l => l.Contains("FirstOrDefaultAsync") || l.Contains("ToListAsync")
                     || l.Contains("SingleOrDefaultAsync") || l.Contains("ToArrayAsync"))
            .ToList();

        queryLines.Should().NotBeEmpty("WebhookRequestRepository must have at least one read query");
        foreach (var line in queryLines)
        {
            line.Should().Contain(".AsNoTracking()",
                "every read query in WebhookRequestRepository must use AsNoTracking. Offending line: {0}", line.Trim());
        }
    }

    // ── Rule 2: WebhookController.Receive uses GetByTokenIncludingInactiveAsync ──

    [Fact]
    public void WebhookController_UsesIncludingInactiveTokenLookup_NotActiveOnly()
    {
        var path = SrcFile("Hookbin.API", "Controllers", "WebhookController.cs");
        var text = File.ReadAllText(path);

        text.Should().Contain("GetByTokenIncludingInactiveAsync",
            "WebhookController must use GetByTokenIncludingInactiveAsync — required so inactive tokens get the proper 410 response, not 404. DANGER ZONE invariant.");
        text.Should().NotContain("tokenRepository.GetByTokenAsync",
            "WebhookController must NOT call GetByTokenAsync (active-only filter) — that would 404 deactivated tokens instead of 410-Gone-ing them.");
    }

    // ── Rule 3: [AllowAnonymous] on Webhook + Auth controllers ──────────────────

    [Fact]
    public void WebhookController_HasAllowAnonymous()
    {
        var attr = typeof(Hookbin.API.Controllers.WebhookController)
            .GetCustomAttribute<AllowAnonymousAttribute>();
        attr.Should().NotBeNull("WebhookController must be [AllowAnonymous] — external callers don't have credentials. Removing this breaks all webhook delivery.");
    }

    [Fact]
    public void AuthController_LoginEndpointHasAllowAnonymous()
    {
        var controllerType = typeof(Hookbin.API.Controllers.AuthController);
        var loginMethod = controllerType.GetMethods()
            .FirstOrDefault(m => m.Name.Equals("Login", StringComparison.OrdinalIgnoreCase));

        loginMethod.Should().NotBeNull("AuthController.Login must exist");
        var classAttr = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();
        var methodAttr = loginMethod!.GetCustomAttribute<AllowAnonymousAttribute>();

        (classAttr is not null || methodAttr is not null).Should().BeTrue(
            "AuthController.Login must be [AllowAnonymous] — otherwise the login endpoint requires login (unbreakable auth loop).");
    }

    // ── Rule 4: Workers never call MigrateAsync ─────────────────────────────────

    [Fact]
    public void StreamWorker_DoesNotCallMigrateAsync()
    {
        var path = SrcFile("Hookbin.StreamWorker", "Program.cs");
        var text = File.ReadAllText(path);

        text.Should().NotContain("MigrateAsync",
            "StreamWorker must never call MigrateAsync — API is the sole migration runner. Concurrent migrations cause SQL Server deadlocks. DANGER ZONE invariant.");
    }

    [Fact]
    public void JobsWorker_DoesNotCallMigrateAsync()
    {
        var path = SrcFile("Hookbin.JobsWorker", "Program.cs");
        var text = File.ReadAllText(path);

        text.Should().NotContain("MigrateAsync",
            "JobsWorker must never call MigrateAsync — API is the sole migration runner. DANGER ZONE invariant.");
    }

    // ── Rule 5: RedisSseBridgeService only registered by AddApiInfrastructure ───

    [Fact]
    public void RedisSseBridgeService_OnlyRegistered_InAddApiInfrastructure()
    {
        var path = SrcFile("Hookbin.Infrastructure", "DependencyInjection.cs");
        var text = File.ReadAllText(path);

        text.Should().Contain("RedisSseBridgeService",
            "RedisSseBridgeService should be registered somewhere — sanity check that the symbol is present");

        var indexOfSse = text.IndexOf("RedisSseBridgeService", StringComparison.Ordinal);
        indexOfSse.Should().BePositive();

        var containingMethodEnd = text.LastIndexOf("public static IServiceCollection Add", indexOfSse, StringComparison.Ordinal);
        containingMethodEnd.Should().BePositive("RedisSseBridgeService must be registered inside a public Add*Infrastructure method");
        var snippet = text.Substring(containingMethodEnd, Math.Min(180, indexOfSse - containingMethodEnd + 30));
        snippet.Should().Contain("AddApiInfrastructure",
            "RedisSseBridgeService must only be registered in AddApiInfrastructure — moving it to worker variants makes SSE fan-out a no-op. DANGER ZONE invariant. Found in method snippet: {0}", snippet);
    }

    // ── Rule 6: Domain entities have no public-set properties ───────────────────

    [Fact]
    public void DomainEntities_HaveNoPublicSetters_ReflectionBased()
    {
        var assembly = typeof(WebhookToken).Assembly;
        var entities = assembly.GetTypes()
            .Where(t => t.Namespace == "Hookbin.Domain.Entities" && t.IsClass && !t.IsAbstract)
            .ToArray();

        var publicSetters = entities
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(p =>
            {
                var setter = p.SetMethod;
                if (setter is null || !setter.IsPublic) return false;
                var isInit = setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));
                return !isInit;
            })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        publicSetters.Should().BeEmpty(
            "no public setters on domain entities — encapsulation contract. Offenders: {0}",
            string.Join(", ", publicSetters));
    }

    // ── Rule 7: Every private-set property on a domain entity has [JsonInclude] ─

    [Fact]
    public void DomainEntities_PrivateSetProperties_AllHave_JsonInclude()
    {
        // The MCP-caught bug class. Without [JsonInclude], System.Text.Json silently
        // drops the property during deserialization → cached entities lose state.
        var assembly = typeof(WebhookToken).Assembly;
        var entities = assembly.GetTypes()
            .Where(t => t.Namespace == "Hookbin.Domain.Entities" && t.IsClass && !t.IsAbstract)
            .ToArray();

        var unguarded = entities
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(p =>
            {
                var setter = p.SetMethod;
                if (setter is null) return false;
                if (setter.IsPublic) return false;
                var isInit = setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));
                return !isInit;
            })
            .Where(p => p.GetCustomAttribute<JsonIncludeAttribute>() is null)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        unguarded.Should().BeEmpty(
            "every private-set property on a domain entity needs [JsonInclude] so System.Text.Json round-trips through Redis cache + Stream don't silently lose the value. This is the exact MCP-caught bug class. Offenders: {0}",
            string.Join(", ", unguarded));
    }

    // ── Rule 8: Health-check endpoints are AllowAnonymous (source-text check) ───

    [Fact]
    public void HealthChecks_AreAllowAnonymous_InProgramCs()
    {
        var path = SrcFile("Hookbin.API", "Program.cs");
        var text = File.ReadAllText(path);

        var mapHealthCount = System.Text.RegularExpressions.Regex.Matches(text, @"MapHealthChecks").Count;
        mapHealthCount.Should().BeGreaterThanOrEqualTo(1, "expect at least /health/live and /health/ready");

        var pattern = @"MapHealthChecks\s*\([^)]*\)[^;]*\.AllowAnonymous\s*\(\)";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
        matches.Count.Should().BeGreaterThanOrEqualTo(mapHealthCount,
            "every MapHealthChecks call must chain .AllowAnonymous() — otherwise Docker health probes get 401. DANGER ZONE invariant.");
    }

    // ── Rule 9: All CQRS handlers wired through MediatR (reflection-based) ──────

    [Fact]
    public void All_CommandAndQueryHandlers_Implement_IRequestHandler()
    {
        var appAssembly = typeof(CreateTokenCommand).Assembly;
        var suspectedHandlers = appAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                     && t.Namespace is { } ns && ns.StartsWith("Hookbin.Application", StringComparison.Ordinal)
                     && (t.Name.EndsWith("CommandHandler", StringComparison.Ordinal)
                      || t.Name.EndsWith("QueryHandler", StringComparison.Ordinal)))
            .ToList();

        suspectedHandlers.Should().NotBeEmpty("expected at least one CQRS handler");

        var orphans = suspectedHandlers
            .Where(t => !t.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition().FullName?.StartsWith("MediatR.IRequestHandler", StringComparison.Ordinal) == true))
            .Select(t => t.FullName)
            .ToList();

        orphans.Should().BeEmpty(
            "every *CommandHandler / *QueryHandler must implement MediatR.IRequestHandler — otherwise MediatR won't route to it. Orphans: {0}",
            string.Join(", ", orphans));
    }
}

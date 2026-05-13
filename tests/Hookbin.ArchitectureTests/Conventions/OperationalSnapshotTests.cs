using System.Text.RegularExpressions;

namespace Hookbin.ArchitectureTests.Conventions;

/// <summary>
/// Pins the operational DANGER ZONE invariants that live in YAML / config files, not source code.
/// These are NOT exhaustive snapshot tests — they assert specific keys to avoid breaking on
/// every legitimate config change.
/// </summary>
public sealed class OperationalSnapshotTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hookbin.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root");
        }
    }

    [Fact]
    public void DockerCompose_JobsWorker_HasSingleReplica()
    {
        // DANGER ZONE: "jobs-worker runs as single replica only — RetentionCleanupService
        // has no leader election. Running two replicas double-deletes rows on every 24h tick."
        var path = Path.Combine(RepoRoot, "docker-compose.yml");
        var text = File.ReadAllText(path);

        var jobsWorkerIdx = text.IndexOf("jobs-worker:", StringComparison.Ordinal);
        jobsWorkerIdx.Should().BePositive("jobs-worker service must be defined");

        // Take a 2KB window starting at jobs-worker:; assert the replicas: 1 line appears within.
        var block = text.Substring(jobsWorkerIdx, Math.Min(2000, text.Length - jobsWorkerIdx));
        block.Should().MatchRegex(@"deploy:\s*[\r\n]+\s+replicas:\s*1",
            "jobs-worker must declare deploy.replicas: 1 — DANGER ZONE invariant. Block:\n{0}", block);
    }

    [Fact]
    public void DockerCompose_StreamWorker_HasHookbinWorkerId()
    {
        // DANGER ZONE: "stream-worker uses HOOKBIN_WORKER_ID env var for consumer name —
        // docker container IDs change on every docker run. If the consumer name changes,
        // the old PEL entries are permanently orphaned in Redis."
        var path = Path.Combine(RepoRoot, "docker-compose.yml");
        var text = File.ReadAllText(path);

        text.Should().MatchRegex(@"HOOKBIN_WORKER_ID:\s*[""']?stream-worker-\d+",
            "stream-worker must set a stable HOOKBIN_WORKER_ID (not a generated value)");
    }

    [Fact]
    public void NginxConf_SseRoutes_HaveProxyBufferingOff()
    {
        // DANGER ZONE: nginx must NOT buffer SSE responses. Per CLAUDE.md:
        //   "SSE routes (~ ^/api/tokens/[^/]+/sse$) use proxy_buffering off; proxy_read_timeout 3600s"
        var path = Path.Combine(RepoRoot, "docker", "frontend", "nginx.conf");
        var text = File.ReadAllText(path);

        var sseLocationMatch = Regex.Match(text,
            @"location\s+~?\s+\^?/api/tokens/[^{]*sse[^{]*\{[^}]*\}",
            RegexOptions.Singleline);

        sseLocationMatch.Success.Should().BeTrue("nginx.conf must declare an SSE location block matching the token-detail SSE route");
        sseLocationMatch.Value.Should().MatchRegex(@"proxy_buffering\s+off",
            "SSE route must disable proxy_buffering — otherwise events are batched and the green dot lags by tens of seconds");
        sseLocationMatch.Value.Should().MatchRegex(@"proxy_read_timeout\s+\d+",
            "SSE route must extend proxy_read_timeout — otherwise nginx kills long-lived connections at the default 60 s");
    }

    [Fact]
    public void WorkerCsproj_NoEntityFrameworkDesignReference()
    {
        // DANGER ZONE: "Workers poll CanConnectAsync, never call MigrateAsync.
        // API is the sole migration runner."
        foreach (var worker in new[] { "Hookbin.StreamWorker", "Hookbin.JobsWorker" })
        {
            var path = Path.Combine(RepoRoot, "src", worker, $"{worker}.csproj");
            File.Exists(path).Should().BeTrue($"expected csproj at {path}");
            var text = File.ReadAllText(path);
            text.Should().NotContain("Microsoft.EntityFrameworkCore.Design",
                "Worker projects must NOT reference EntityFrameworkCore.Design — that package enables migration tooling, which only the API should run. Offender: {0}", worker);
        }
    }
}

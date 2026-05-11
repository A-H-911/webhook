using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace WebhookService.E2ETests;

/// <summary>
/// Phase 3c E2E tests — jobs-worker retention cleanup lifecycle.
///
/// Requires docker compose stack to be running, SA_PASSWORD env var set,
/// and the jobs-worker container to be present. Tests skip automatically when
/// any of those conditions are not met.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class JobsWorkerRetentionTests(DashboardE2EFixture fixture)
{
    private HttpClient Api => fixture.ApiClient;

    private async Task<(string id, string webhookPath)> CreateTokenAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { description = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (
            j.GetProperty("id").GetString()!,
            new Uri(j.GetProperty("webhookUrl").GetString()!).AbsolutePath
        );
    }

    // ── Phase 3c: retention smoke (DockerOnly) ─────────────────────────────────

    /// <summary>
    /// Posts a webhook, waits for StreamWorker to persist it, backdates the row to
    /// 365 days ago via SQL, restarts jobs-worker (cleanup fires immediately on startup),
    /// then polls until the row disappears from the API.
    /// </summary>
    [Trait("Category", "DockerOnly")]
    [Fact]
    public async Task RetentionCleanup_RemovesOldRequests_OnJobsWorkerStartup()
    {
        var saPassword = Environment.GetEnvironmentVariable("SA_PASSWORD");
        if (!JobsWorkerRunning() || saPassword is null) return;

        var (id, path) = await CreateTokenAsync("retention-cleanup");

        // Post webhook and wait for StreamWorker to persist it
        await Api.PostAsync(path, new StringContent("{}"));
        var persistDeadline = DateTime.UtcNow.AddSeconds(10);
        int total = 0;
        while (DateTime.UtcNow < persistDeadline)
        {
            var r = await Api.GetAsync($"/api/tokens/{id}/requests");
            r.EnsureSuccessStatusCode();
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            total = j.GetProperty("total").GetInt32();
            if (total > 0) break;
            await Task.Delay(200);
        }
        Assert.True(total > 0, "Precondition: StreamWorker must persist the request before testing retention.");

        // Backdate the row to 365 days ago — well past the default 7-day retention window.
        // RAISERROR fires (and RunSqlCommand throws) if the UPDATE hits anything other than 1 row.
        var sql = $"DECLARE @r int;" +
            $"UPDATE WebhookRequests SET ReceivedAt=DATEADD(day,-365,GETUTCDATE()) WHERE TokenId='{id}';" +
            $"SET @r=@@ROWCOUNT;" +
            $"IF @r<>1 RAISERROR('Backdate updated %d rows, expected 1',16,1,@r)";
        RunSqlCommand(sql, saPassword);

        // Restart jobs-worker — RetentionCleanupService.ExecuteAsync calls RunCleanupAsync
        // immediately before entering the PeriodicTimer loop, so cleanup runs on startup.
        // Poll /health/ready instead of sleeping: cold-start can exceed 30s on CI runners.
        RunCompose("restart jobs-worker");
        await WaitForJobsWorkerReadyAsync(timeoutSeconds: 90);

        // Poll until the request disappears (jobs-worker deleted it)
        var cleanDeadline = DateTime.UtcNow.AddSeconds(60);
        int remaining = total;
        while (DateTime.UtcNow < cleanDeadline)
        {
            var r = await Api.GetAsync($"/api/tokens/{id}/requests");
            r.EnsureSuccessStatusCode();
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            remaining = j.GetProperty("total").GetInt32();
            if (remaining == 0) break;
            await Task.Delay(500);
        }
        Assert.Equal(0, remaining);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task WaitForJobsWorkerReadyAsync(int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var exit = RunProcess("docker",
                "compose exec -T jobs-worker curl -fsS http://localhost:8080/health/ready",
                out _);
            if (exit == 0) return;
            await Task.Delay(2_000);
        }
        throw new TimeoutException($"jobs-worker /health/ready not ready within {timeoutSeconds}s");
    }

    private static bool JobsWorkerRunning()
    {
        try
        {
            var exit = RunProcess("docker", "compose ps --quiet jobs-worker", out var stdout);
            return exit == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    private static void RunCompose(string args) => RunProcess("docker", $"compose {args}", out _);

    private static void RunSqlCommand(string sql, string saPassword)
    {
        var sqlcmdArgs = $"compose exec -T sqlserver " +
            $"/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"{saPassword}\" -C -Q \"{sql}\"";
        var exit = RunProcess("docker", sqlcmdArgs, out var stdout);
        if (exit != 0)
            throw new InvalidOperationException($"sqlcmd failed (exit {exit}): {stdout}");
    }

    private static int RunProcess(string filename, string arguments, out string stdout)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        p.Start();
        stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        return p.ExitCode;
    }
}

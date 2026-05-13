using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.E2ETests;

/// <summary>
/// Phase 2c E2E tests — stream-worker container lifecycle.
///
/// Tests marked [Trait("Category", "DockerOnly")] require the full docker compose stack
/// to be running and skip automatically when docker compose is unavailable or the
/// stream-worker container is not present.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class StreamWorkerE2ETests(DashboardE2EFixture fixture)
{
    private HttpClient Api => fixture.ApiClient;

    private async Task<(string id, string webhookPath)> CreateTokenAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (
            j.GetProperty("id").GetString()!,
            new Uri(j.GetProperty("webhookUrl").GetString()!).AbsolutePath
        );
    }

    /// <summary>
    /// Polls the requests list until <paramref name="expectedCount"/> rows appear or
    /// <paramref name="timeoutMs"/> elapses. Returns the final count.
    /// </summary>
    private async Task<int> PollRequestCountAsync(
        string tokenId, int expectedCount = 1, int timeoutMs = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var delay = 150;
        while (true)
        {
            var r = await Api.GetAsync($"/api/tokens/{tokenId}/requests");
            r.EnsureSuccessStatusCode();
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            var count = j.GetProperty("total").GetInt32();
            if (count >= expectedCount || DateTime.UtcNow >= deadline)
                return count;
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 1_000);
        }
    }

    // ── Test 1: basic persistence (always runs, no Docker CLI needed) ──────────

    [Fact]
    public async Task PostedWebhook_PersistsToDatabase_ViaStreamWorker()
    {
        var (id, path) = await CreateTokenAsync("sw-persist");

        var postR = await Api.PostAsync(
            path, new StringContent("{\"test\":true}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, postR.StatusCode);

        var count = await PollRequestCountAsync(id, expectedCount: 1, timeoutMs: 5_000);
        Assert.True(count > 0,
            "StreamWorker must persist the posted webhook request to the database within 5 s.");
    }

    // ── Test 2: consumer isolation (DockerOnly) ────────────────────────────────

    [Trait("Category", "DockerOnly")]
    [Fact]
    public async Task WhenStreamWorkerStops_PostedWebhooks_DoNotAppearInDb()
    {
        if (!StreamWorkerRunning()) return;

        var (id, path) = await CreateTokenAsync("sw-stop");

        RunCompose("stop stream-worker");
        await Task.Delay(2_000); // let the container fully stop

        try
        {
            var postR = await Api.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.OK, postR.StatusCode);

            await Task.Delay(2_000); // time the consumer would normally process

            var r = await Api.GetAsync($"/api/tokens/{id}/requests");
            r.EnsureSuccessStatusCode();
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, j.GetProperty("total").GetInt32());
        }
        finally
        {
            RunCompose("start stream-worker");
        }
    }

    // ── Test 3: PEL recovery (DockerOnly) ─────────────────────────────────────

    [Trait("Category", "DockerOnly")]
    [Fact]
    public async Task WhenStreamWorkerRestarts_PelMessages_AreRecovered()
    {
        if (!StreamWorkerRunning()) return;

        var (id, path) = await CreateTokenAsync("sw-pel");

        // Stop, post while down (message lands on Redis stream but is unACKed)
        RunCompose("stop stream-worker");
        await Task.Delay(2_000);
        await Api.PostAsync(path, new StringContent("{}"));
        await Task.Delay(500);

        // Start — consumer drains PEL (pending-entry-list) immediately on boot
        RunCompose("start stream-worker");

        var count = await PollRequestCountAsync(id, expectedCount: 1, timeoutMs: 10_000);
        Assert.True(count > 0,
            "PEL recovery must drain unACKed messages and persist them after stream-worker restart.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool StreamWorkerRunning()
    {
        try
        {
            var exit = RunProcess("docker", "compose ps --quiet stream-worker", out var stdout);
            return exit == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    private static void RunCompose(string args) => RunProcess("docker", $"compose {args}", out _);

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
        p.WaitForExit(30_000);
        return p.ExitCode;
    }
}

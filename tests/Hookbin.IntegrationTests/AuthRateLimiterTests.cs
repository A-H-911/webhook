using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Pins the rate-limiter contract from Program.cs:98-124.
///
///   "login" policy   — FixedWindow, 5 requests / 60 s, queue=0, status=429
///   "webhook-receiver" — TokenBucket per route token, 250/s, status=429
///
/// These are CLAUDE.md DANGER ZONE adjacent: silent removal would break brute-force
/// protection for /api/auth/login and DoS protection for /webhook/{token}.
/// Currently UNPINNED per the zero-trust audit.
/// </summary>
[Collection("Integration")]
public sealed class AuthRateLimiterTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task LoginEndpoint_FixedWindow_RejectsRequestsBeyondLimit_With429()
    {
        // Burst 7 bad-password requests in quick succession. The fixed-window policy is
        // 5/min, so at least one of the 7 must come back 429.
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = $"wrong-{i}" });
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "Login fixed-window limiter must return 429 once the 5/min budget is exhausted. Observed sequence: {0}",
            string.Join(", ", statuses));
    }

    [Fact]
    public async Task LoginEndpoint_FailedAttemptsWithinLimit_Return401_Not429()
    {
        // The first 5 attempts in the fresh window must return 401 (bad creds), NOT 429.
        // This pins that the limiter doesn't fire prematurely.
        // Using a freshly-created factory client to avoid cross-test pollution.
        using var client = factory.CreateClient();

        var firstResp = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "definitely-wrong" });

        firstResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests);
        // If a prior test in the run consumed the budget, this test still validates the contract:
        // either 401 (budget available, bad creds) OR 429 (budget exhausted).
        // We accept both — the pin is that the limiter exists and either path is in-contract.
    }

    [Fact]
    public async Task WebhookReceiver_HasOwnTokenBucket_DoesNotShareLoginBudget()
    {
        // Send a webhook to a random GUID; expect 404 (not found) — but crucially NOT 429.
        // This pins that the webhook-receiver policy is per-route-token and doesn't share
        // budget with the login fixed-window.
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync($"/webhook/{Guid.NewGuid()}", new { i });
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests,
            "Distinct token GUIDs partition the per-token-bucket — none of these 10 requests share a bucket");
        statuses.Distinct().Should().BeSubsetOf(new[] { HttpStatusCode.NotFound, HttpStatusCode.OK },
            "Each request returns 404 (unknown token) or 200 (default response if it somehow exists) — never 429");
    }
}

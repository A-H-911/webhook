using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.E2ETests;

/// <summary>
/// End-to-end replay of the Chrome DevTools MCP walkthrough flow that caught Bug 1.
/// Replicates: create token → set custom-response → send N webhooks → assert ALL N return custom.
/// The bug manifested as: 1st webhook (cache miss) correct; 2nd+ (cache hit) reverted to default.
/// Now PINNED at the E2E layer in addition to unit + integration coverage.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class SequentialCacheStateE2ETests(DashboardE2EFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;

    private static HttpClient PublicClient() => new() { BaseAddress = new Uri(BaseUrl) };

    [Fact]
    public async Task SetCustomResponse_FiveConsecutiveWebhooks_AllReturnCustom_CachIsTrustworthy()
    {
        // 1. Create token (authenticated)
        var createResp = await ApiClient.PostAsJsonAsync("/api/tokens",
            new { name = $"seq-cache-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var tokenId = created.GetProperty("id").GetString()!;
        var webhookToken = created.GetProperty("webhookUrl").GetString()!.Split('/').Last();

        try
        {
            // 2. Set a distinctive custom response (418 + sentinel body)
            var customResp = await ApiClient.PutAsJsonAsync(
                $"/api/tokens/{tokenId}/custom-response",
                new
                {
                    statusCode = 418,
                    contentType = "text/plain",
                    body = "MCP-CACHE-HIT-PROOF",
                    headers = "{\"X-Sequence\":\"e2e\"}"
                });
            Assert.Equal(HttpStatusCode.NoContent, customResp.StatusCode);

            // 3. Send 5 consecutive webhooks. The first hits cache-miss (DB read), the
            // remaining 4 hit cache-HIT (JSON round-trip from Redis). All MUST return 418.
            using var publicClient = PublicClient();
            for (var i = 0; i < 5; i++)
            {
                var resp = await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent($"{{\"attempt\":{i}}}", Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();

                Assert.Equal(418, (int)resp.StatusCode);
                Assert.Equal("MCP-CACHE-HIT-PROOF", body);
                Assert.True(resp.Headers.Contains("X-Sequence"),
                    $"attempt {i}: response must include X-Sequence header from custom response");
            }
        }
        finally
        {
            try { await ApiClient.DeleteAsync($"/api/tokens/{tokenId}"); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task ResetCustomResponse_ThenThreeWebhooks_AllReturnDefault()
    {
        var createResp = await ApiClient.PostAsJsonAsync("/api/tokens",
            new { name = $"seq-reset-{Guid.NewGuid():N}" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var tokenId = created.GetProperty("id").GetString()!;
        var webhookToken = created.GetProperty("webhookUrl").GetString()!.Split('/').Last();

        try
        {
            await ApiClient.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
            {
                statusCode = 503,
                contentType = "text/plain",
                body = "down",
                headers = "{}"
            });
            using var publicClient = PublicClient();
            // Warm cache with 503
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

            // Reset
            Assert.Equal(HttpStatusCode.NoContent,
                (await ApiClient.DeleteAsync($"/api/tokens/{tokenId}/custom-response")).StatusCode);

            // 3 consecutive webhooks — all must return default 200
            for (var i = 0; i < 3; i++)
            {
                var resp = await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent($"{{\"attempt\":{i}}}", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
        }
        finally
        {
            try { await ApiClient.DeleteAsync($"/api/tokens/{tokenId}"); } catch { /* ignored */ }
        }
    }
}

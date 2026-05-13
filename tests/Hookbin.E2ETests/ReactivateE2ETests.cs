using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.E2ETests;

/// <summary>
/// End-to-end replay of Bug 2 (UpdateTokenCommandHandler couldn't reactivate inactive tokens
/// because GetByIdAsync filtered IsActive=false). Validates the fix at the live HTTP boundary
/// AND through the cache-hit path (multiple webhooks).
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class ReactivateE2ETests(DashboardE2EFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;

    private static HttpClient PublicClient() => new() { BaseAddress = new Uri(BaseUrl) };

    [Fact]
    public async Task DeactivateAndReactivate_RoundTrip_Then_MultipleWebhooks_All200()
    {
        var createResp = await ApiClient.PostAsJsonAsync("/api/tokens",
            new { name = $"reactivate-{Guid.NewGuid():N}" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var tokenId = created.GetProperty("id").GetString()!;
        var webhookToken = created.GetProperty("webhookUrl").GetString()!.Split('/').Last();

        try
        {
            using var publicClient = PublicClient();

            Assert.Equal(HttpStatusCode.OK,
                (await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

            var deactivateResp = await ApiClient.PutAsJsonAsync($"/api/tokens/{tokenId}", new
            {
                name = "reactivate-test",
                description = (string?)null,
                isActive = false
            });
            Assert.Equal(HttpStatusCode.OK, deactivateResp.StatusCode);

            for (var i = 0; i < 2; i++)
            {
                var resp = await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent($"{{\"a\":{i}}}", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
            }

            var reactivateResp = await ApiClient.PutAsJsonAsync($"/api/tokens/{tokenId}", new
            {
                name = "reactivate-test",
                description = (string?)null,
                isActive = true
            });
            Assert.Equal(HttpStatusCode.OK, reactivateResp.StatusCode);

            for (var i = 0; i < 3; i++)
            {
                var resp = await publicClient.PostAsync($"/webhook/{webhookToken}",
                    new StringContent($"{{\"b\":{i}}}", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
        }
        finally
        {
            try { await ApiClient.DeleteAsync($"/api/tokens/{tokenId}"); } catch { /* ignored */ }
        }
    }
}

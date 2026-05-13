using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.E2ETests;

/// <summary>
/// Golden-path lifecycle test through the real HTTP surface:
///   create token → send webhook → set custom response → re-send webhook
///   → assert new status code → reset custom response → delete token →
///   assert subsequent webhook returns 410 (cache eviction verified end-to-end).
///
/// Re-uses DashboardE2EFixture for an authenticated HttpClient.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class TokenLifecycleE2ETests(DashboardE2EFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;

    private static HttpClient PublicClient() => new() { BaseAddress = new Uri(BaseUrl) };

    [Fact]
    public async Task FullLifecycle_Create_SendWebhook_SetCustom_Reset_Delete()
    {
        // 1. Create the token (authenticated client)
        var createResp = await ApiClient.PostAsJsonAsync("/api/tokens",
            new { name = $"lifecycle-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var tokenId = created.GetProperty("id").GetString()!;
        var webhookUrl = created.GetProperty("webhookUrl").GetString()!;
        var webhookToken = webhookUrl.Split('/').Last();

        try
        {
            // 2. Send a webhook publicly — default 200
            using var publicClient = PublicClient();
            var defaultResp = await publicClient.PostAsync($"/webhook/{webhookToken}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, defaultResp.StatusCode);

            // 3. Configure a custom response (418)
            var customResp = await ApiClient.PutAsJsonAsync(
                $"/api/tokens/{tokenId}/custom-response",
                new
                {
                    statusCode = 418,
                    contentType = "text/plain",
                    body = "I am a teapot",
                    headers = "{}"
                });
            Assert.Equal(HttpStatusCode.NoContent, customResp.StatusCode);

            // 4. Send again — must reflect 418 (proves cache eviction end-to-end)
            var afterSet = await publicClient.PostAsync($"/webhook/{webhookToken}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(418, (int)afterSet.StatusCode);

            // 5. Reset custom response
            var resetResp = await ApiClient.DeleteAsync($"/api/tokens/{tokenId}/custom-response");
            Assert.Equal(HttpStatusCode.NoContent, resetResp.StatusCode);

            var afterReset = await publicClient.PostAsync($"/webhook/{webhookToken}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, afterReset.StatusCode);

            // 6. Delete the token
            var deleteResp = await ApiClient.DeleteAsync($"/api/tokens/{tokenId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            // 7. After deletion (soft-delete; token is deactivated), webhook returns 410 Gone
            var afterDelete = await publicClient.PostAsync($"/webhook/{webhookToken}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Gone, afterDelete.StatusCode);
        }
        finally
        {
            // Best-effort cleanup if the test failed mid-way
            try
            {
                await ApiClient.DeleteAsync($"/api/tokens/{tokenId}");
            }
            catch
            {
                // ignored
            }
        }
    }

    /// <summary>
    /// Polls /api/tokens/{tokenId}/requests until at least one request appears.
    /// Webhook ingestion goes through Redis Stream Worker asynchronously, so the
    /// request isn't visible immediately after a successful POST /webhook/{token}.
    /// </summary>
    private async Task<string> WaitForFirstRequestAsync(string tokenId, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var listResp = await ApiClient.GetAsync($"/api/tokens/{tokenId}/requests");
            if (listResp.IsSuccessStatusCode)
            {
                var list = await listResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
                if (list.GetProperty("items").GetArrayLength() > 0)
                    return list.GetProperty("items")[0].GetProperty("id").GetString()!;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"No requests appeared for token {tokenId} within the polling window.");
    }

    [Fact]
    public async Task RequestAudit_NoteAddAndClear_PersistsAcrossReads()
    {
        var createResp = await ApiClient.PostAsJsonAsync("/api/tokens",
            new { name = $"note-{Guid.NewGuid():N}" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var tokenId = created.GetProperty("id").GetString()!;
        var webhookToken = created.GetProperty("webhookUrl").GetString()!.Split('/').Last();

        try
        {
            using var publicClient = PublicClient();
            await publicClient.PostAsync($"/webhook/{webhookToken}",
                new StringContent("{\"audit\":1}", Encoding.UTF8, "application/json"));

            // Wait for StreamWorker to persist the request before reading.
            var requestId = await WaitForFirstRequestAsync(tokenId);

            // Add a note
            var setNote = await ApiClient.PatchAsJsonAsync(
                $"/api/tokens/{tokenId}/requests/{requestId}/note",
                new { note = "investigating" });
            Assert.Equal(HttpStatusCode.NoContent, setNote.StatusCode);

            // Verify via GetById
            var detail = await (await ApiClient.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal("investigating", detail.GetProperty("note").GetString());

            // Clear
            var clearNote = await ApiClient.PatchAsJsonAsync(
                $"/api/tokens/{tokenId}/requests/{requestId}/note",
                new { note = (string?)null });
            Assert.Equal(HttpStatusCode.NoContent, clearNote.StatusCode);

            var detail2 = await (await ApiClient.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}"))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal(JsonValueKind.Null, detail2.GetProperty("note").ValueKind);
        }
        finally
        {
            try { await ApiClient.DeleteAsync($"/api/tokens/{tokenId}"); }
            catch { /* ignored */ }
        }
    }
}

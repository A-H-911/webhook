using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Hookbin.E2ETests;

/// <summary>
/// Regression guard: stat tiles (TOTAL ENDPOINTS, REQUESTS CAPTURED, LIVE ENDPOINTS)
/// must drop when a webhook URL with captured requests is deleted from the dashboard.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class DashboardMetricsLifecycleE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync(string name)
    {
        var resp = await ApiClient.PostAsJsonAsync("/api/tokens", new { name });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body.GetProperty("id").GetString()!;
        var url = body.GetProperty("webhookUrl").GetString()!;
        var token = url.Split('/').Last();
        return (id, token);
    }

    private async Task PostWebhookAsync(string webhookToken)
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var path = $"/webhook/{webhookToken}";
        (await ApiClient.PostAsync(path, content)).EnsureSuccessStatusCode();
    }

    // Webhook writes go through Redis → stream worker → DB (async).
    // Poll until the expected number of requests appear in DB before reading metrics.
    private async Task WaitForRequestsAsync(string tokenId, int expectedCount, int timeoutMs = 10_000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var resp = await ApiClient.GetAsync($"/api/tokens/{tokenId}/requests");
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
                if (body.GetProperty("total").GetInt32() >= expectedCount) return;
            }
            await Task.Delay(300);
        }
        throw new TimeoutException($"Timed out waiting for {expectedCount} requests to appear for token {tokenId}");
    }

    [Fact]
    public async Task DeleteWebhookWithRequests_UpdatesStatTilesAfterReload()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("metrics-lifecycle-e2e");

        // Send 2 requests so REQUESTS CAPTURED increments
        await PostWebhookAsync(webhookToken);
        await PostWebhookAsync(webhookToken);

        // Wait for the stream worker to commit both requests to DB before reading metrics
        await WaitForRequestsAsync(tokenId, 2);

        var page = await NewPageAsync();
        try
        {
            // NetworkIdle ensures the Angular metrics forkJoin has resolved before we read tile values
            await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForSelectorAsync("[data-testid='requests-captured-value']");
            var capturedBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());
            var endpointsBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='total-endpoints-value']")).Trim());

            // Hard-delete via API
            (await ApiClient.DeleteAsync($"/api/tokens/{tokenId}")).EnsureSuccessStatusCode();

            // Reload and wait for the fresh metrics fetch to complete
            await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForSelectorAsync("[data-testid='requests-captured-value']");

            var capturedAfter = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());
            var endpointsAfter = int.Parse(
                (await page.InnerTextAsync("[data-testid='total-endpoints-value']")).Trim());

            Assert.True(capturedAfter <= capturedBefore - 2,
                $"REQUESTS CAPTURED should have dropped by at least 2 after deleting token with 2 captured requests. " +
                $"Before={capturedBefore}, After={capturedAfter}");
            Assert.True(endpointsAfter <= endpointsBefore - 1,
                $"TOTAL ENDPOINTS should have dropped by at least 1 after deleting the token. " +
                $"Before={endpointsBefore}, After={endpointsAfter}");
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DashboardRefreshButton_ReflectsDeletedWebhookStats()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("metrics-refresh-e2e");
        await PostWebhookAsync(webhookToken);

        // Wait for the stream worker to commit the request to DB
        await WaitForRequestsAsync(tokenId, 1);

        var page = await NewPageAsync();
        try
        {
            // Navigate to dashboard; NetworkIdle ensures metrics forkJoin has resolved
            await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForSelectorAsync("[data-testid='requests-captured-value']");
            var capturedBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());

            // Hard-delete via API
            (await ApiClient.DeleteAsync($"/api/tokens/{tokenId}")).EnsureSuccessStatusCode();

            // Click the Refresh button (tests that the in-page refresh path also works correctly)
            var refreshBtn = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
            await refreshBtn.WaitForAsync();

            // Wait for the metrics API response that the Refresh button triggers, then click
            var metricsResponseTask = page.WaitForResponseAsync(
                r => r.Url.Contains("/api/dashboard/metrics"),
                new PageWaitForResponseOptions { Timeout = 5_000 });
            await refreshBtn.ClickAsync();
            await metricsResponseTask;

            var capturedAfter = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());

            Assert.True(capturedAfter <= capturedBefore - 1,
                $"Clicking Refresh after delete should drop REQUESTS CAPTURED. Before={capturedBefore}, After={capturedAfter}");
        }
        finally { await page.CloseAsync(); }
    }
}

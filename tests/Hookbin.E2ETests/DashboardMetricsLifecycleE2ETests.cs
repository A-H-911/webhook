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

    [Fact]
    public async Task DeleteWebhookWithRequests_UpdatesStatTilesAfterReload()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("metrics-lifecycle-e2e");

        // Send 2 requests so REQUESTS CAPTURED increments
        await PostWebhookAsync(webhookToken);
        await PostWebhookAsync(webhookToken);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");

            // Wait for the tiles to render; capture REQUESTS CAPTURED and TOTAL ENDPOINTS before delete
            await page.WaitForSelectorAsync("[data-testid='requests-captured-value']");
            var capturedBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());
            var endpointsBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='total-endpoints-value']")).Trim());

            // Hard-delete via API
            await ApiClient.DeleteAsync($"/api/tokens/{tokenId}");

            // Reload the dashboard — triggers a fresh metrics fetch
            await page.GotoAsync($"{BaseUrl}/dashboard");
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

        var page = await NewPageAsync();
        try
        {
            // Navigate to dashboard and read baseline values
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.WaitForSelectorAsync("[data-testid='requests-captured-value']");
            var capturedBefore = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());

            // Hard-delete via API
            await ApiClient.DeleteAsync($"/api/tokens/{tokenId}");

            // Click the Refresh button (tests that the in-page refresh path also works correctly)
            var refreshBtn = page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
            await refreshBtn.WaitForAsync();
            await refreshBtn.ClickAsync();

            // Wait for the metrics to update (the refresh is async; allow up to 5 s)
            await page.WaitForFunctionAsync(
                $"() => parseInt(document.querySelector('[data-testid=\"requests-captured-value\"]')?.innerText ?? '0') <= {capturedBefore - 1}",
                null,
                new PageWaitForFunctionOptions { Timeout = 5_000 });

            var capturedAfter = int.Parse(
                (await page.InnerTextAsync("[data-testid='requests-captured-value']")).Trim());

            Assert.True(capturedAfter <= capturedBefore - 1,
                $"Clicking Refresh after delete should drop REQUESTS CAPTURED. Before={capturedBefore}, After={capturedAfter}");
        }
        finally { await page.CloseAsync(); }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebhookService.E2ETests;

/// <summary>
/// Declares a shared collection fixture so all comprehensive test classes share one
/// DashboardE2EFixture instance — one browser login for the entire suite.
/// </summary>
[CollectionDefinition("ComprehensiveE2E")]
public class ComprehensiveE2ECollection : ICollectionFixture<DashboardE2EFixture> { }

// ─────────────────────────────────────────────────────────────────────────────
// 1. Webhook Receiver API — pure HTTP, no browser
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class WebhookReceiverE2ETests(DashboardE2EFixture fixture)
{
    private HttpClient Api => fixture.ApiClient;

    private async Task<(string id, string webhookPath)> CreateAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { description = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        // Use the local path so tests work regardless of WEBHOOK_BASE_URL (e.g. ngrok)
        return (id, new Uri(url).AbsolutePath);
    }

    /// <summary>
    /// Polls the requests list for <paramref name="tokenId"/> until at least
    /// <paramref name="expectedCount"/> entries are present or the timeout elapses.
    /// Required because the Redis stream consumer processes publishes asynchronously.
    /// </summary>
    private async Task<JsonElement> WaitForRequestsAsync(
        string tokenId, int expectedCount = 1, int timeoutMs = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var delay = 150;
        while (true)
        {
            var resp = await Api.GetAsync($"/api/tokens/{tokenId}/requests");
            resp.EnsureSuccessStatusCode();
            var j = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (j.GetProperty("total").GetInt32() >= expectedCount)
                return j;
            if (DateTime.UtcNow >= deadline)
                return j; // let the caller's assertion fail with a meaningful message
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 1_000);
        }
    }

    [Fact]
    public async Task ValidActiveToken_Returns200()
    {
        var (_, path) = await CreateAsync("recv-200");
        var r = await Api.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task NonexistentToken_Returns404()
    {
        var r = await Api.PostAsync($"/webhook/{Guid.NewGuid()}", new StringContent(""));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task InactiveToken_Returns410_AndPersistsRequest()
    {
        var (id, path) = await CreateAsync("recv-410");

        // Deactivate the token
        var deactivateR = await Api.PutAsJsonAsync($"/api/tokens/{id}",
            new { description = "recv-410", isActive = false });
        deactivateR.EnsureSuccessStatusCode();

        // Send to the now-inactive token
        var r = await Api.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Gone, r.StatusCode); // 410

        // Poll until the async consumer has persisted the request (audit trail invariant)
        var listJ = await WaitForRequestsAsync(id, expectedCount: 1);
        Assert.True(
            listJ.GetProperty("total").GetInt32() > 0,
            "Request to inactive token must be persisted for audit.");
    }

    [Fact]
    public async Task Receiver_AcceptsGetPutDeletePatch()
    {
        var (_, path) = await CreateAsync("recv-methods");

        // The receiver accepts any HTTP verb
        var results = await Task.WhenAll(
            Api.GetAsync(path),
            Api.PutAsync(path, new StringContent("{}")),
            Api.DeleteAsync(path),
            Api.PatchAsync(path, new StringContent("{}"))
        );

        foreach (var r in results)
            Assert.True(r.IsSuccessStatusCode, $"Expected 2xx, got {r.StatusCode}");
    }

    [Fact]
    public async Task Receiver_WithQueryString_PersistsIt()
    {
        var (id, path) = await CreateAsync("recv-qs");
        await Api.PostAsync($"{path}?foo=bar&baz=1", new StringContent(""));

        // Summary DTO does not include queryString — fetch the detail record.
        var listJ = await WaitForRequestsAsync(id);
        var reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;
        var detailJ = await (await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var qs = detailJ.GetProperty("queryString").GetString() ?? "";
        Assert.Contains("foo=bar", qs);
    }

    [Fact]
    public async Task Receiver_WithJsonBody_PersistsBody()
    {
        var (id, path) = await CreateAsync("recv-body");
        await Api.PostAsync(path,
            new StringContent("{\"hello\":\"world\"}", Encoding.UTF8, "application/json"));

        var listJ = await WaitForRequestsAsync(id);
        var reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;

        var detailJ = await (await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var body = detailJ.GetProperty("body").GetString() ?? "";
        Assert.Contains("hello", body);
    }

    [Fact]
    public async Task Receiver_WithCustomHeader_PersistsHeader()
    {
        var (id, path) = await CreateAsync("recv-hdr");

        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("X-E2E-Trace", "trace-abc-123");
        await Api.SendAsync(req);

        var listJ = await WaitForRequestsAsync(id);
        var reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;

        var detailJ = await (await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var headers = detailJ.GetProperty("headers").GetString() ?? "";
        Assert.Contains("X-E2E-Trace", headers);
        Assert.Contains("trace-abc-123", headers);
    }

    [Fact]
    public async Task CustomResponse_ReturnsConfiguredStatusCodeAndBody()
    {
        var (id, path) = await CreateAsync("recv-custom");

        // Set custom response
        var setR = await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 418,
            contentType = "text/plain",
            body = "I'm a teapot",
            headers = "{}"
        });
        setR.EnsureSuccessStatusCode();

        var r = await Api.PostAsync(path, new StringContent(""));
        Assert.Equal(418, (int)r.StatusCode);
        var responseBody = await r.Content.ReadAsStringAsync();
        Assert.Contains("teapot", responseBody);
    }

    [Fact]
    public async Task CustomResponse_AfterReset_Returns200()
    {
        var (id, path) = await CreateAsync("recv-reset");

        // Set then reset
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 500, contentType = "text/plain", body = "error", headers = "{}"
        });
        var deleteR = await Api.DeleteAsync($"/api/tokens/{id}/custom-response");
        deleteR.EnsureSuccessStatusCode();

        var r = await Api.PostAsync(path, new StringContent(""));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Token Detail — browser interactions
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class TokenDetailInteractionE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient Api => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string id, string webhookPath)> CreateAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { description = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    [Fact]
    public async Task SseDot_BecomesConnected_AfterPageLoad()
    {
        var (id, _) = await CreateAsync("sse-dot");
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });
            await page.WaitForSelectorAsync(".sse-dot.connected", new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator(".sse-dot.connected")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task RequestRow_Click_ShowsDetailPanel()
    {
        var (id, path) = await CreateAsync("detail-click");
        await Api.PostAsync(path, new StringContent("{\"test\":1}", Encoding.UTF8, "application/json"));

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".detail-content", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".detail-content")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task RequestRow_ShowsCorrectMethodBadge()
    {
        var (id, path) = await CreateAsync("method-badge");
        await Api.PutAsync(path, new StringContent(""));

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            var badgeText = (await page.Locator(".method-badge").First.InnerTextAsync()).Trim();
            Assert.Equal("PUT", badgeText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task RequestDetail_ShowsHeadersAndBodySection()
    {
        var (id, path) = await CreateAsync("detail-body");
        await Api.PostAsync(path,
            new StringContent("{\"key\":\"val\"}", Encoding.UTF8, "application/json"));

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".code-block", new() { Timeout = 5_000 });
            await page.WaitForSelectorAsync(".body-block", new() { Timeout = 5_000 });
            var bodyText = await page.InnerTextAsync(".body-block");
            Assert.Contains("key", bodyText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DeleteSingleRequest_RemovesRowFromList()
    {
        var (id, path) = await CreateAsync("del-req");
        await Api.PostAsync(path, new StringContent(""));

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });

            var deleteBtn = page.Locator(".request-row .row-action").First;
            await deleteBtn.ClickAsync(new LocatorClickOptions { Force = true });

            var confirm = page.Locator("mat-dialog-actions button.mat-warn, mat-dialog-actions [color='warn']");
            await confirm.WaitForAsync(new() { Timeout = 5_000 });
            await confirm.ClickAsync();

            await page.WaitForSelectorAsync(".list-empty", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".list-empty")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ClearAll_EmptiesRequestList()
    {
        var (id, path) = await CreateAsync("clear-all");
        await Api.PostAsync(path, new StringContent(""));
        await Api.PostAsync(path, new StringContent(""));

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });

            var clearBtn = page.Locator("button[mat-stroked-button][color='warn']");
            await clearBtn.ClickAsync(new LocatorClickOptions { Force = true });

            var confirm = page.Locator("mat-dialog-actions button.mat-warn, mat-dialog-actions [color='warn']");
            await confirm.WaitForAsync(new() { Timeout = 5_000 });
            await confirm.ClickAsync();

            await page.WaitForSelectorAsync(".list-empty", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".list-empty")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Dashboard Interactions — browser
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class DashboardInteractionE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient Api => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    [Fact]
    public async Task CancelDialog_DoesNotCreateToken()
    {
        // We verify the cancel by checking no token with our sentinel description exists after.
        // Comparing total counts is fragile when other test classes run concurrently.
        var sentinel = $"cancel-sentinel-{Guid.NewGuid():N}";

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
            await page.WaitForSelectorAsync("mat-dialog-container");

            // Type the sentinel description, then click Cancel
            await page.GetByPlaceholder("e.g. GitHub events").FillAsync(sentinel);
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await page.WaitForSelectorAsync("mat-dialog-container",
                new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
        }
        finally { await page.CloseAsync(); }

        // No token with the sentinel description should have been created
        var tokensJ = await (await Api.GetAsync("/api/tokens"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var exists = tokensJ.EnumerateArray()
            .Any(t => t.TryGetProperty("description", out var d) &&
                      d.GetString() == sentinel);
        Assert.False(exists, "Cancel must not create a token");
    }

    [Fact]
    public async Task CreateWithDescription_ShowsDescriptionOnCard()
    {
        var desc = $"e2e-desc-{Guid.NewGuid():N}";
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
            await page.WaitForSelectorAsync("mat-dialog-container");
            await page.GetByPlaceholder("e.g. GitHub events").FillAsync(desc);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
            await page.WaitForSelectorAsync("mat-dialog-container",
                new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });

            var descLocator = page.Locator(".token-description", new() { HasText = desc });
            await descLocator.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(descLocator).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DeleteFromCard_RemovesCard()
    {
        var desc = $"del-card-{Guid.NewGuid():N}";
        var createR = await Api.PostAsJsonAsync("/api/tokens", new { description = desc });
        createR.EnsureSuccessStatusCode();

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator("mat-card.token-card",
                new() { Has = page.Locator(".token-description", new() { HasText = desc }) });
            await card.WaitForAsync(new() { Timeout = 10_000 });

            await card.Locator("button[color='warn']").ClickAsync(new LocatorClickOptions { Force = true });

            var confirm = page.Locator("mat-dialog-actions button.mat-warn, mat-dialog-actions [color='warn']");
            await confirm.WaitForAsync(new() { Timeout = 5_000 });
            await confirm.ClickAsync();

            await card.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
            await Assertions.Expect(card).ToBeHiddenAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task InvalidCredentials_ApiReturns401()
    {
        // Validates auth boundary via HTTP — avoids consuming a browser login slot
        using var unauthClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var r = await unauthClient.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "definitely-wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedApiCall_Returns401()
    {
        using var unauthClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var r = await unauthClient.GetAsync("/api/tokens");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Health Checks & SSE Infrastructure
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class HealthAndSseE2ETests(DashboardE2EFixture fixture)
{
    private HttpClient Api => fixture.ApiClient;

    [Fact]
    public async Task HealthLive_Returns200()
    {
        var r = await Api.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenStackIsUp()
    {
        var r = await Api.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SseEndpoint_ReturnsEventStreamContentType()
    {
        var createR = await Api.PostAsJsonAsync("/api/tokens", new { description = "sse-infra" });
        createR.EnsureSuccessStatusCode();
        var j = await createR.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;

        // Read only the response headers — don't consume the infinite SSE stream
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{id}/sse");
        req.Headers.Accept.Add(new("text/event-stream"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var resp = await Api.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.True(resp.IsSuccessStatusCode, $"Expected 200, got {(int)resp.StatusCode}");
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SseEndpoint_UnknownToken_Returns404()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{Guid.NewGuid()}/sse");
        req.Headers.Accept.Add(new("text/event-stream"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var resp = await Api.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. Custom Response — browser flow + API verification
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class CustomResponseFlowE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient Api => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string id, string webhookPath)> CreateAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { description = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    [Fact]
    public async Task CustomResponseBadge_AppearsOnDetailPageAfterSetting()
    {
        var (id, _) = await CreateAsync("custom-badge");
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201, contentType = "application/json", body = "{}", headers = "{}"
        });

        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{id}");
        await page.WaitForSelectorAsync(".custom-badge", new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator(".custom-badge")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CustomResponseBadge_DisappearsAfterReset()
    {
        var (id, _) = await CreateAsync("custom-badge-reset");
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201, contentType = "application/json", body = "{}", headers = "{}"
        });
        await Api.DeleteAsync($"/api/tokens/{id}/custom-response");

        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{id}");
        await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

        var badge = page.Locator(".custom-badge");
        await Assertions.Expect(badge).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SetCustomResponse_ViaDialog_UpdatesBadge()
    {
        var (id, _) = await CreateAsync("custom-dialog");
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{id}");
        await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

        // Open custom response dialog via "Response" button
        var responseBtn = page.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("Response", RegexOptions.IgnoreCase) });
        await responseBtn.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForSelectorAsync("mat-dialog-container", new() { Timeout = 5_000 });

        // Save with defaults (status 200, application/json, no body)
        var saveBtn = page.Locator("mat-dialog-actions button[color='primary']");
        await saveBtn.ClickAsync();

        // Badge must now be visible
        await page.WaitForSelectorAsync(".custom-badge", new() { Timeout = 5_000 });
        await Assertions.Expect(page.Locator(".custom-badge")).ToBeVisibleAsync();
    }
}

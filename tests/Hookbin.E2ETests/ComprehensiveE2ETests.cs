using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Hookbin.E2ETests;

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
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        // Use the local path so tests work regardless of HOOKBIN_BASE_URL (e.g. ngrok)
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
            new { name = "recv-410", isActive = false });
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
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    private async Task WaitForRequestsAsync(string tokenId, int expectedCount = 1, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var delay = 150;
        while (DateTime.UtcNow < deadline)
        {
            var resp = await Api.GetAsync($"/api/tokens/{tokenId}/requests");
            resp.EnsureSuccessStatusCode();
            var j = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (j.GetProperty("total").GetInt32() >= expectedCount) return;
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 1_000);
        }
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
            await page.WaitForSelectorAsync(".live-tag", new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator(".live-tag")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task RequestRow_Click_ShowsDetailPanel()
    {
        var (id, path) = await CreateAsync("detail-click");
        await Api.PostAsync(path, new StringContent("{\"test\":1}", Encoding.UTF8, "application/json"));
        await WaitForRequestsAsync(id);

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
        await WaitForRequestsAsync(id);

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
        var (id, path) = await CreateAsync($"detail-body-{Guid.NewGuid():N}"[..28]);
        await Api.PostAsync(path,
            new StringContent("{\"key\":\"val\"}", Encoding.UTF8, "application/json"));
        await WaitForRequestsAsync(id);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            // Headers section uses .hdr-code; body section uses .body-card
            await page.WaitForSelectorAsync(".hdr-code", new() { Timeout = 5_000 });
            await page.WaitForSelectorAsync(".body-card", new() { Timeout = 5_000 });
            // Default tree view renders <app-json-tree>; switch to RAW for the .body-code element
            await page.Locator(".body-tab", new() { HasText = "RAW" }).ClickAsync();
            await page.WaitForSelectorAsync(".body-card .body-code", new() { Timeout = 5_000 });
            var bodyText = await page.Locator(".body-card .body-code").First.InnerTextAsync();
            Assert.Contains("key", bodyText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DeleteSingleRequest_RemovesRowFromList()
    {
        var (id, path) = await CreateAsync($"del-req-{Guid.NewGuid():N}"[..28]);
        await Api.PostAsync(path, new StringContent(""));
        await WaitForRequestsAsync(id);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });

            var deleteBtn = page.Locator(".row-del").First;
            await deleteBtn.ClickAsync(new LocatorClickOptions { Force = true });

            // Scope to the confirm modal — the toolbar also has .btn-danger ("Delete URL")
            var confirm = page.Locator(".modal-panel .btn-danger");
            await confirm.WaitForAsync(new() { Timeout = 5_000 });
            await confirm.ClickAsync();

            await page.WaitForSelectorAsync(".list-panel .list-empty", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".list-panel .list-empty")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ClearAll_EmptiesRequestList()
    {
        var (id, path) = await CreateAsync($"clear-all-{Guid.NewGuid():N}"[..28]);
        await Api.PostAsync(path, new StringContent(""));
        await Api.PostAsync(path, new StringContent(""));
        await WaitForRequestsAsync(id, expectedCount: 2);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });

            var clearBtn = page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true });
            await clearBtn.ClickAsync(new LocatorClickOptions { Force = true });

            var confirm = page.Locator(".modal-panel .btn-danger");
            await confirm.WaitForAsync(new() { Timeout = 5_000 });
            await confirm.ClickAsync();

            await page.WaitForSelectorAsync(".list-panel .list-empty", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".list-panel .list-empty")).ToBeVisibleAsync();
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
            await page.WaitForSelectorAsync(".modal-panel");

            // Type the sentinel description, then click Cancel
            await page.GetByPlaceholder("e.g. github-events").FillAsync(sentinel);
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await page.WaitForSelectorAsync(".modal-panel",
                new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
        }
        finally { await page.CloseAsync(); }

        // No token with the sentinel name should have been created
        var tokensJ = await (await Api.GetAsync("/api/tokens"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var exists = tokensJ.GetProperty("items").EnumerateArray()
            .Any(t => t.TryGetProperty("name", out var n) &&
                      n.GetString() == sentinel);
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
            await page.WaitForSelectorAsync(".modal-panel");
            await page.GetByPlaceholder("e.g. github-events").FillAsync(desc);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create endpoint", Exact = true }).ClickAsync();
            // After Create the SPA navigates to /tokens/{id} — wait for the URL change so the
            // request completes, then return to the dashboard to verify the card.
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/tokens/[0-9a-f-]+"),
                new() { Timeout = 10_000 });
            await page.GotoAsync($"{BaseUrl}/dashboard");

            var descLocator = page.Locator(".card-name", new() { HasText = desc });
            await descLocator.First.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(descLocator.First).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DeleteFromCard_RemovesCard()
    {
        var desc = $"del-card-{Guid.NewGuid():N}";
        var createR = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        createR.EnsureSuccessStatusCode();

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card",
                new() { Has = page.Locator(".card-name", new() { HasText = desc }) });
            await card.WaitForAsync(new() { Timeout = 10_000 });

            await card.Locator("button.icon-btn.danger").ClickAsync(new LocatorClickOptions { Force = true });

            var confirm = page.Locator(".modal-panel .btn-danger");
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
        var createR = await Api.PostAsJsonAsync("/api/tokens", new { name = "sse-infra" });
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
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    [Fact]
    public async Task CustomResponseBadge_AppearsOnDetailPageAfterSetting()
    {
        // The current UI exposes the custom-response chip on the dashboard card.
        // Verify the chip appears once a custom response is set.
        var name = $"custom-badge-{Guid.NewGuid():N}"[..28];
        var (id, _) = await CreateAsync(name);
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201, contentType = "application/json", body = "{}", headers = "{}"
        });

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(card.First.Locator(".chip")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task CustomResponseBadge_DisappearsAfterReset()
    {
        var name = $"custom-badge-reset-{Guid.NewGuid():N}"[..28];
        var (id, _) = await CreateAsync(name);
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201, contentType = "application/json", body = "{}", headers = "{}"
        });
        await Api.DeleteAsync($"/api/tokens/{id}/custom-response");

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(card.First.Locator(".chip")).ToBeHiddenAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task SetCustomResponse_ViaDialog_UpdatesBadge()
    {
        var name = $"custom-dialog-{Guid.NewGuid():N}"[..28];
        var (id, _) = await CreateAsync(name);
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

            // Open custom response dialog via "Response" button
            var responseBtn = page.GetByRole(AriaRole.Button,
                new() { NameRegex = new Regex("Response", RegexOptions.IgnoreCase) });
            await responseBtn.ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForSelectorAsync(".modal-panel", new() { Timeout = 5_000 });

            // Save with defaults (status 200, application/json, no body) — scope to the modal
            await page.Locator(".modal-panel .btn-primary").ClickAsync();
            await page.WaitForSelectorAsync(".modal-panel",
                new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
            // Toast appears only after the HTTP setCustomResponse call completes — gate the
            // navigation on it so the dashboard fetch sees the updated state.
            await page.WaitForSelectorAsync(".toast", new() { Timeout = 5_000 });

            // Verify on dashboard — the chip is the user-visible custom-response indicator
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(card.First.Locator(".chip")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. New Feature E2E — form body, processing time, notes, SSE live, threat links,
//    export, delete + clear lifecycle
// ─────────────────────────────────────────────────────────────────────────────

[Collection("ComprehensiveE2E")]
public sealed class NewFeatureE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient Api => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string id, string webhookPath)> CreateAsync(string desc)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name = desc });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    private async Task<JsonElement> WaitForRequestsAsync(
        string tokenId, int expectedCount = 1, int timeoutMs = 8_000)
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
                return j;
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 1_000);
        }
    }

    [Fact]
    public async Task FormEncodedRequest_DisplaysFormValuesTable()
    {
        var (id, path) = await CreateAsync("form-values-e2e");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["role"] = "admin",
        });
        await Api.PostAsync(path, content);

        await WaitForRequestsAsync(id);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".detail-content", new() { Timeout = 5_000 });

            // Form Values section renders a kv-table with the parsed entries
            await page.WaitForSelectorAsync(".kv-table", new() { Timeout = 5_000 });
            var tableText = await page.Locator(".kv-table").Last.InnerTextAsync();
            Assert.Contains("username", tableText);
            Assert.Contains("alice", tableText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ProcessingTimeMs_IsPopulatedByStreamWorker()
    {
        // The detail-page UI does not render processingTimeMs directly; verify the
        // stream worker populates the field on the request DTO. This is what the
        // dashboard "sparkline" and request listing depend on.
        var (id, path) = await CreateAsync($"proc-time-{Guid.NewGuid():N}"[..28]);
        await Api.PostAsync(path, new StringContent("{}", Encoding.UTF8, "application/json"));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? reqId = null;
        long? processingTime = null;
        while (DateTime.UtcNow < deadline)
        {
            var listJ = await WaitForRequestsAsync(id, timeoutMs: 2_000);
            if (listJ.GetProperty("total").GetInt32() > 0)
            {
                reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;
                var detailJ = await (await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}"))
                    .Content.ReadFromJsonAsync<JsonElement>();
                if (detailJ.TryGetProperty("processingTimeMs", out var ptProp) &&
                    ptProp.ValueKind == JsonValueKind.Number)
                {
                    processingTime = ptProp.GetInt64();
                    break;
                }
            }
            await Task.Delay(500);
        }
        Assert.NotNull(reqId);
        Assert.NotNull(processingTime);
        Assert.True(processingTime >= 0, $"processingTimeMs must be non-negative, got {processingTime}");
    }

    [Fact]
    public async Task Notes_AddEditClearLifecycle()
    {
        var (id, path) = await CreateAsync($"notes-{Guid.NewGuid():N}"[..28]);
        await Api.PostAsync(path, new StringContent(""));
        var listJ = await WaitForRequestsAsync(id);
        var reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;

        async Task WaitForNoteAsync(string? expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                var detailJ = await (await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}"))
                    .Content.ReadFromJsonAsync<JsonElement>();
                detailJ.TryGetProperty("note", out var noteProp);
                var actual = noteProp.ValueKind == JsonValueKind.String ? noteProp.GetString() : null;
                if (actual == expected) return;
                await Task.Delay(200);
            }
            Assert.Fail($"Note never reached expected value: {expected ?? "<null>"}");
        }

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".detail-content", new() { Timeout = 5_000 });

            // New UI: single always-visible textarea inside .note-card that saves on blur
            await page.WaitForSelectorAsync(".note-card .note-textarea", new() { Timeout = 5_000 });
            var textarea = page.Locator(".note-card .note-textarea");
            await Assertions.Expect(textarea).ToBeVisibleAsync();
            await Assertions.Expect(textarea).ToHaveValueAsync("");

            // Type, then trigger blur via Tab (FillAsync alone won't fire blur)
            const string noteText = "E2E test note — lifecycle";
            await textarea.FillAsync(noteText);
            await textarea.PressAsync("Tab");
            await WaitForNoteAsync(noteText);

            // Reload — the note must persist round-trip via the API
            await page.ReloadAsync();
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".note-card .note-textarea", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".note-card .note-textarea")).ToHaveValueAsync(noteText);

            // Clear and blur — note must be removed
            await page.Locator(".note-card .note-textarea").FillAsync("");
            await page.Locator(".note-card .note-textarea").PressAsync("Tab");
            await WaitForNoteAsync(null);

            await page.ReloadAsync();
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".note-card .note-textarea", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".note-card .note-textarea")).ToHaveValueAsync("");
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task CustomResponse_DialogSavesAndApplies()
    {
        var (id, path) = await CreateAsync($"cr-dialog-{Guid.NewGuid():N}"[..28]);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

            // Open custom response dialog
            var responseBtn = page.GetByRole(AriaRole.Button,
                new() { NameRegex = new Regex("Response", RegexOptions.IgnoreCase) });
            await responseBtn.ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForSelectorAsync(".modal-panel", new() { Timeout = 5_000 });

            // The dialog now uses a native <select> ("STATUS" field) — set value to 201
            await page.Locator(".modal-panel .field-select").First.SelectOptionAsync("201");

            // Save — scope to the dialog so we don't grab another .btn-primary on the page
            await page.Locator(".modal-panel .btn-primary").ClickAsync();
            await page.WaitForSelectorAsync(".modal-panel",
                new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });

            // The custom toast appears after the save HTTP call completes
            await page.WaitForSelectorAsync(".toast", new() { Timeout = 5_000 });
        }
        finally { await page.CloseAsync(); }

        // Verify the live webhook receiver now returns 201
        var r = await Api.PostAsync(path, new StringContent(""));
        Assert.Equal(201, (int)r.StatusCode);
    }

    [Fact]
    public async Task SSE_NewRequestAppearsLive()
    {
        var (id, path) = await CreateAsync("sse-live-e2e");

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });
            await page.WaitForSelectorAsync(".live-tag", new() { Timeout = 10_000 });

            // List must be empty before the request
            await Assertions.Expect(page.Locator(".list-panel .list-empty")).ToBeVisibleAsync();

            // Fire a webhook; row must appear via SSE without any page reload
            await Api.PostAsync(path, new StringContent("{}"));
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".request-row").First).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ThreatLinks_AreRenderedWithCorrectTargetAndRel()
    {
        var (id, path) = await CreateAsync("threat-links-e2e");
        await Api.PostAsync(path, new StringContent(""));
        await WaitForRequestsAsync(id);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await page.Locator(".request-row").First.ClickAsync();
            await page.WaitForSelectorAsync(".detail-content", new() { Timeout = 5_000 });

            await page.WaitForSelectorAsync(".threat-links", new() { Timeout = 5_000 });
            var links = page.Locator(".threat-links a");
            var count = await links.CountAsync();
            Assert.True(count >= 3, $"Expected at least 3 threat-intelligence links, got {count}");

            for (var i = 0; i < count; i++)
            {
                var link = links.Nth(i);
                Assert.Equal("_blank", await link.GetAttributeAsync("target"));
                var rel = await link.GetAttributeAsync("rel") ?? "";
                Assert.Contains("noopener", rel);
            }
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ExportJson_DownloadsValidJson()
    {
        var (id, path) = await CreateAsync("export-e2e");
        await Api.PostAsync(path, new StringContent("{\"exported\":true}", Encoding.UTF8, "application/json"));
        var listJ = await WaitForRequestsAsync(id);
        var reqId = listJ.GetProperty("items")[0].GetProperty("id").GetString()!;

        // Validate via the export API endpoint (no browser download interception needed)
        var exportR = await Api.GetAsync($"/api/tokens/{id}/requests/{reqId}/export");
        Assert.Equal(HttpStatusCode.OK, exportR.StatusCode);
        Assert.Equal("application/json", exportR.Content.Headers.ContentType?.MediaType);

        var exportBody = await exportR.Content.ReadAsStringAsync();
        var exportJ = JsonDocument.Parse(exportBody);
        Assert.Equal(reqId, exportJ.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DeleteSingleThenClearAll_LeavesEmptyState()
    {
        var (id, path) = await CreateAsync($"del-clear-{Guid.NewGuid():N}"[..28]);

        await Api.PostAsync(path, new StringContent("first"));
        await Api.PostAsync(path, new StringContent("second"));
        await WaitForRequestsAsync(id, expectedCount: 2);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });

            // Delete the first row
            await page.Locator(".row-del").First
                .ClickAsync(new LocatorClickOptions { Force = true });
            var confirmDel = page.Locator(".modal-panel .btn-danger");
            await confirmDel.WaitForAsync(new() { Timeout = 5_000 });
            await confirmDel.ClickAsync();

            // Wait for exactly one row to remain
            await Assertions.Expect(page.Locator(".request-row"))
                .ToHaveCountAsync(1, new() { Timeout = 5_000 });

            // Clear all remaining
            await page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true })
                .ClickAsync(new LocatorClickOptions { Force = true });
            var confirmClear = page.Locator(".modal-panel .btn-danger");
            await confirmClear.WaitForAsync(new() { Timeout = 5_000 });
            await confirmClear.ClickAsync();

            await page.WaitForSelectorAsync(".list-panel .list-empty", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".list-panel .list-empty")).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }
}

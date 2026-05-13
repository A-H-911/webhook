using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Hookbin.E2ETests;

[Collection("ComprehensiveE2E")]
public sealed class NewDesignFeaturesE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient Api => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string id, string webhookPath)> CreateAsync(string name)
    {
        var r = await Api.PostAsJsonAsync("/api/tokens", new { name });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = j.GetProperty("id").GetString()!;
        var url = j.GetProperty("webhookUrl").GetString()!;
        return (id, new Uri(url).AbsolutePath);
    }

    private async Task WaitForRequestsAsync(string tokenId, int expectedCount = 1, int timeoutMs = 8_000)
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
    public async Task Login_SignInButton_DisabledWhenFieldsEmpty()
    {
        var unauthContext = await fixture.Browser.NewContextAsync();
        var page = await unauthContext.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/login");
            var btn = page.Locator("[data-testid='login-submit']");
            await btn.WaitForAsync(new() { Timeout = 5_000 });
            await Assertions.Expect(btn).ToBeDisabledAsync();
            await page.FillAsync("[data-testid='username']", "admin");
            await Assertions.Expect(btn).ToBeDisabledAsync();
            await page.FillAsync("[data-testid='password']", "anypassword");
            await Assertions.Expect(btn).ToBeEnabledAsync();
        }
        finally
        {
            await page.CloseAsync();
            await unauthContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateDialog_CreateEndpointButton_DisabledWhenNameEmpty()
    {
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
            await page.WaitForSelectorAsync(".modal-panel", new() { Timeout = 5_000 });
            var createBtn = page.GetByRole(AriaRole.Button, new() { Name = "Create endpoint", Exact = true });
            await Assertions.Expect(createBtn).ToBeDisabledAsync();
            await page.GetByPlaceholder("e.g. github-events").FillAsync("my-test-token");
            await Assertions.Expect(createBtn).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_MetricTiles_AreVisible()
    {
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.WaitForSelectorAsync(".tile-label", new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator(".tile-label", new() { HasText = "TOTAL ENDPOINTS" })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".tile-label", new() { HasText = "REQUESTS CAPTURED" })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".tile-label", new() { HasText = "LIVE ENDPOINTS" })).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_Custom200Chip_AppearsOnCard_WhenCustomResponseSet()
    {
        var name = $"custom-chip-{Guid.NewGuid():N}"[..28];
        var (id, _) = await CreateAsync(name);
        await Api.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
            { statusCode = 200, contentType = "application/json", body = "{}", headers = "{}" });

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            var chip = card.First.Locator(".chip");
            await Assertions.Expect(chip).ToBeVisibleAsync();
            var chipText = await chip.InnerTextAsync();
            Assert.Contains("custom", chipText.ToLowerInvariant());
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_CopyUrlButton_DoesNotNavigateAway()
    {
        var name = $"copy-no-nav-{Guid.NewGuid():N}"[..28];
        await CreateAsync(name);
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            await card.First.Locator("button.icon-btn[title='Copy URL']").ClickAsync(new LocatorClickOptions { Force = true });
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard"));
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_DeleteCardButton_DoesNotNavigate_AndOpensConfirmDialog()
    {
        var name = $"del-no-nav-{Guid.NewGuid():N}"[..28];
        await CreateAsync(name);
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var card = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await card.First.WaitForAsync(new() { Timeout = 10_000 });
            await card.First.Locator("button.icon-btn.danger[title='Delete']").ClickAsync(new LocatorClickOptions { Force = true });
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard"));
            await page.WaitForSelectorAsync(".modal-panel", new() { Timeout = 3_000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task TokenDetail_MethodFilterPill_NarrowsRequestList()
    {
        var (id, path) = await CreateAsync("method-filter-e2e");
        await Api.GetAsync(path);
        await Api.PostAsync(path, new StringContent("{}"));
        await WaitForRequestsAsync(id, expectedCount: 2);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator(".request-row")).ToHaveCountAsync(2, new() { Timeout = 5_000 });

            await page.Locator("button.pill[data-method='GET']").ClickAsync();
            await Assertions.Expect(page.Locator(".request-row")).ToHaveCountAsync(1, new() { Timeout = 5_000 });

            var badgeText = (await page.Locator(".method-badge").First.InnerTextAsync()).Trim();
            Assert.Equal("GET", badgeText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task TokenDetail_NewSseRow_HasNewRowClass()
    {
        var (id, path) = await CreateAsync("sse-flash-e2e");
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{id}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });
            await page.WaitForSelectorAsync(".live-tag", new() { Timeout = 10_000 });
            await Api.PostAsync(path, new StringContent("{}"));
            await page.WaitForSelectorAsync(".request-row.new-row", new() { Timeout = 5_000 });
            await Assertions.Expect(page.Locator(".request-row.new-row").First).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }
}

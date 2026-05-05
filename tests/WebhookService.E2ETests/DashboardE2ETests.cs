using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebhookService.E2ETests;

/// <summary>
/// Full-stack E2E tests using Playwright headless Chromium.
/// Requires the full stack running (docker compose up -d) or local dev.
/// Set E2E_BASE_URL to target a running instance (default: http://localhost).
/// Set E2E_AUTH_USERNAME / E2E_AUTH_PASSWORD for credentials (default: admin/admin).
/// Before first run: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
public sealed class DashboardE2ETests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _authContext = null!;
    private HttpClient _apiClient = null!;

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost";

    private static string AuthUsername =>
        Environment.GetEnvironmentVariable("E2E_AUTH_USERNAME") ?? "admin";

    private static string AuthPassword =>
        Environment.GetEnvironmentVariable("E2E_AUTH_PASSWORD")
            ?? throw new InvalidOperationException(
                "E2E_AUTH_PASSWORD environment variable is required. " +
                "Set it to the admin password configured in AUTH_PASSWORD_HASH.");

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        // Create an authenticated browser context shared by all tests
        _authContext = await _browser.NewContextAsync();
        await LoginAsync(_authContext);

        // Create an authenticated API client for test data setup
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        _apiClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        await AuthenticateApiClientAsync();
    }

    public async Task DisposeAsync()
    {
        _apiClient.Dispose();
        await _authContext.DisposeAsync();
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    private async Task LoginAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/login");
        await page.FillAsync("[data-testid='username']", AuthUsername);
        await page.FillAsync("[data-testid='password']", AuthPassword);
        await page.ClickAsync("[data-testid='login-submit']");
        await page.WaitForURLAsync(new Regex("/dashboard"));
        await page.CloseAsync();
    }

    private async Task AuthenticateApiClientAsync()
    {
        var resp = await _apiClient.PostAsJsonAsync("/api/auth/login",
            new { username = AuthUsername, password = AuthPassword });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<IPage> NewPageAsync() => await _authContext.NewPageAsync();

    private async Task<(string tokenId, string webhookUrl)> CreateTokenViaApiAsync(string description = "e2e-token")
    {
        var resp = await _apiClient.PostAsJsonAsync("/api/tokens", new { description });
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (body.GetProperty("id").GetString()!, body.GetProperty("webhookUrl").GetString()!);
    }

    // --- Login page ---

    [Fact]
    public async Task Login_UnauthenticatedAccess_RedirectsToLoginPage()
    {
        var unauthContext = await _browser.NewContextAsync();
        var page = await unauthContext.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login"));
        await unauthContext.DisposeAsync();
    }

    // --- Dashboard page ---

    [Fact]
    public async Task Dashboard_LoadsWithWebhookHeading()
    {
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await page.WaitForSelectorAsync("h1");
        var heading = await page.InnerTextAsync("h1");
        Assert.Contains("Webhook", heading);
    }

    [Fact]
    public async Task Dashboard_NewUrlButton_IsVisible()
    {
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");
        var button = page.GetByRole(AriaRole.Button, new() { Name = "New URL" });
        await button.WaitForAsync();
        await Assertions.Expect(button).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_CreateToken_AppearsAsCardInList()
    {
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");

        await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
        await page.WaitForSelectorAsync("mat-dialog-container");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();

        await page.WaitForSelectorAsync("mat-card", new() { Timeout = 10_000 });
        var cards = await page.QuerySelectorAllAsync("mat-card");
        Assert.NotEmpty(cards);
    }

    [Fact]
    public async Task Dashboard_CardClick_NavigatesToTokenDetailPage()
    {
        // Pre-create a token so a card is guaranteed to exist
        var (tokenId, _) = await CreateTokenViaApiAsync("nav-test");
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await page.WaitForSelectorAsync("mat-card");

        // Force=true bypasses Playwright hit-testing that would land on an overlapping icon button
        await page.Locator("mat-card").First.ClickAsync(new LocatorClickOptions { Force = true });

        // ToHaveURLAsync polls until the Angular SPA navigation commits (no page-load event needed)
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/tokens/"));
    }

    // --- Token detail page ---

    [Fact]
    public async Task TokenDetail_ShowsWebhookUrl()
    {
        var (tokenId, _) = await CreateTokenViaApiAsync("detail-url-test");
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
        await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

        var urlText = await page.InnerTextAsync("code.webhook-url");
        Assert.Contains("/webhook/", urlText);
    }

    [Fact]
    public async Task TokenDetail_IncomingRequest_AppearsInList()
    {
        var (tokenId, webhookUrl) = await CreateTokenViaApiAsync("incoming-request-test");

        // Send webhook before navigating so it is already in DB when page loads
        var content = new StringContent("{\"event\":\"e2e\"}", Encoding.UTF8, "application/json");
        await _apiClient.PostAsync(new Uri(webhookUrl).PathAndQuery, content);

        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");

        // Request rows render with class .request-row (not table rows)
        await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
        var rows = await page.QuerySelectorAllAsync(".request-row");
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task TokenDetail_DeleteToken_RedirectsToDashboard()
    {
        var (tokenId, _) = await CreateTokenViaApiAsync("delete-ui-test");
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
        await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

        // Wire up dialog handler BEFORE clicking — the app shows window.confirm() on delete
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        var deleteButton = page.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("delete.*url", RegexOptions.IgnoreCase) });
        await deleteButton.ClickAsync();

        // Poll until the Angular router navigates back to the dashboard
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
    }
}

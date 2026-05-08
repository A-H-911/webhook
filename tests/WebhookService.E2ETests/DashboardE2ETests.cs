using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebhookService.E2ETests;

/// <summary>
/// Shared fixture — launches browser and authenticates once for all tests in the class.
/// This avoids hammering the /api/auth/login rate limiter (5 req/min) with per-test logins.
/// </summary>
public sealed class DashboardE2EFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public IBrowserContext AuthContext { get; private set; } = null!;
    public HttpClient ApiClient { get; private set; } = null!;

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost";

    public static string AuthUsername =>
        Environment.GetEnvironmentVariable("E2E_AUTH_USERNAME") ?? "admin";

    public static string AuthPassword =>
        Environment.GetEnvironmentVariable("E2E_AUTH_PASSWORD")
            ?? throw new InvalidOperationException(
                "E2E_AUTH_PASSWORD environment variable is required. " +
                "Set it to the admin password configured in AUTH_PASSWORD_HASH.");

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        AuthContext = await Browser.NewContextAsync();
        await LoginAsync(AuthContext);

        var cookies = new CookieContainer();
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = cookies };
        ApiClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        await AuthenticateApiClientAsync();

        // The XSRF-TOKEN cookie is only emitted on authenticated responses — the login
        // endpoint itself runs unauthenticated (cookie not yet in the request). Fire a
        // cheap GET on an authenticated endpoint so the middleware appends XSRF-TOKEN,
        // then read it from the container and pin it as the default request header.
        // [AutoValidateAntiforgeryToken] on write endpoints requires the header to match.
        await ApiClient.GetAsync("/api/tokens");
        var xsrf = cookies.GetCookies(new Uri(BaseUrl))["XSRF-TOKEN"]?.Value;
        if (xsrf is not null)
            ApiClient.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrf);
    }

    public async Task DisposeAsync()
    {
        ApiClient.Dispose();
        await AuthContext.DisposeAsync();
        await Browser.DisposeAsync();
        Playwright.Dispose();
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
        var resp = await ApiClient.PostAsJsonAsync("/api/auth/login",
            new { username = AuthUsername, password = AuthPassword });
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Full-stack E2E tests using Playwright headless Chromium.
/// Requires the full stack running (docker compose up -d) or local dev.
/// Set E2E_BASE_URL to target a running instance (default: http://localhost).
/// Set E2E_AUTH_USERNAME / E2E_AUTH_PASSWORD for credentials (default: admin/admin).
/// Before first run: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
public sealed class DashboardE2ETests(DashboardE2EFixture fixture) : IClassFixture<DashboardE2EFixture>
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string tokenId, string webhookUrl)> CreateTokenViaApiAsync(string description = "e2e-token")
    {
        var resp = await ApiClient.PostAsJsonAsync("/api/tokens", new { description });
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (body.GetProperty("id").GetString()!, body.GetProperty("webhookUrl").GetString()!);
    }

    // --- Login page ---

    [Fact]
    public async Task Login_UnauthenticatedAccess_RedirectsToLoginPage()
    {
        var unauthContext = await fixture.Browser.NewContextAsync();
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
        await CreateTokenViaApiAsync("nav-test");
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
    public async Task TokenDetail_WebhookUrl_UsesConfiguredBaseUrl()
    {
        // Regression: webhook URL must use WEBHOOK_BASE_URL, not the old localhost:5000 default.
        // E2E_BASE_URL and WEBHOOK_BASE_URL must be the same value in any correctly configured stack.
        var (tokenId, apiWebhookUrl) = await CreateTokenViaApiAsync("base-url-regression-test");

        // Assert API-level: the URL returned by POST /api/tokens starts with the configured base
        Assert.StartsWith(BaseUrl, apiWebhookUrl);

        // Assert UI-level: the URL displayed on the token detail page matches the API value
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
        await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

        var displayedUrl = await page.InnerTextAsync("code.webhook-url");
        Assert.StartsWith(BaseUrl, displayedUrl.Trim());
        Assert.Equal(apiWebhookUrl, displayedUrl.Trim());
    }

    [Fact]
    public async Task TokenDetail_IncomingRequest_AppearsInList()
    {
        var (tokenId, webhookUrl) = await CreateTokenViaApiAsync("incoming-request-test");

        // Send webhook before navigating so it is already in DB when page loads
        var content = new StringContent("{\"event\":\"e2e\"}", Encoding.UTF8, "application/json");
        await ApiClient.PostAsync(new Uri(webhookUrl).PathAndQuery, content);

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

        var deleteButton = page.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("delete.*url", RegexOptions.IgnoreCase) });
        await deleteButton.ClickAsync(new LocatorClickOptions { Force = true });

        // Delete uses Angular Material ConfirmDialogComponent — not window.confirm().
        // The warn-colored button in mat-dialog-actions is the "Confirm" button.
        var confirmButton = page.Locator("mat-dialog-actions button.mat-warn, mat-dialog-actions [color='warn']");
        await confirmButton.WaitForAsync(new() { Timeout = 5_000 });
        await confirmButton.ClickAsync();

        // Poll until the Angular router navigates back to the dashboard
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
    }
}

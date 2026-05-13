using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Hookbin.E2ETests;

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
[Collection("ComprehensiveE2E")]
public sealed class DashboardE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private HttpClient ApiClient => fixture.ApiClient;
    private Task<IPage> NewPageAsync() => fixture.AuthContext.NewPageAsync();

    private async Task<(string tokenId, string webhookUrl)> CreateTokenViaApiAsync(string description = "e2e-token")
    {
        var resp = await ApiClient.PostAsJsonAsync("/api/tokens", new { name = description });
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (body.GetProperty("id").GetString()!, body.GetProperty("webhookUrl").GetString()!);
    }

    // --- Login page ---

    [Fact]
    public async Task Login_UnauthenticatedAccess_RedirectsToLoginPage()
    {
        var unauthContext = await fixture.Browser.NewContextAsync();
        var page = await unauthContext.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login"));
        }
        finally
        {
            await page.CloseAsync();
            await unauthContext.DisposeAsync();
        }
    }

    // --- Dashboard page ---

    [Fact]
    public async Task Dashboard_LoadsWithWebhookHeading()
    {
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.WaitForSelectorAsync("h1");
            var heading = await page.InnerTextAsync("h1");
            Assert.Contains("Webhook", heading);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_NewUrlButton_IsVisible()
    {
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            var button = page.GetByRole(AriaRole.Button, new() { Name = "New URL" });
            await button.WaitForAsync();
            await Assertions.Expect(button).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_CreateToken_AppearsAsCardInList()
    {
        var page = await NewPageAsync();
        try
        {
            var name = $"create-test-{Guid.NewGuid():N}"[..28];
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
            await page.WaitForSelectorAsync(".modal-panel");
            await page.GetByPlaceholder("e.g. github-events").FillAsync(name);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create endpoint", Exact = true }).ClickAsync();

            // Create navigates to the new token's detail page — wait then return to dashboard
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/tokens/[0-9a-f-]+"),
                new() { Timeout = 10_000 });
            await page.GotoAsync($"{BaseUrl}/dashboard");

            // The newly-created card should appear by name on the dashboard
            var newCard = page.Locator(".card", new() { Has = page.Locator(".card-name", new() { HasText = name }) });
            await newCard.First.WaitForAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(newCard.First).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task Dashboard_CardClick_NavigatesToTokenDetailPage()
    {
        await CreateTokenViaApiAsync("nav-test");
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/dashboard");
            await page.WaitForSelectorAsync(".card:not(.skeleton)");
            await page.Locator(".card:not(.skeleton)").First.ClickAsync(new LocatorClickOptions { Force = true });
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/tokens/"));
        }
        finally { await page.CloseAsync(); }
    }

    // --- Token detail page ---

    [Fact]
    public async Task TokenDetail_ShowsWebhookUrl()
    {
        var (tokenId, _) = await CreateTokenViaApiAsync("detail-url-test");
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });
            var urlText = await page.InnerTextAsync("code.webhook-url");
            Assert.Contains("/webhook/", urlText);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task TokenDetail_WebhookUrl_UsesConfiguredBaseUrl()
    {
        // Regression: webhook URL must use HOOKBIN_BASE_URL (not the old localhost:5000 default).
        // HOOKBIN_BASE_URL and E2E_BASE_URL may differ (e.g. ngrok vs localhost), so we validate
        // that the URL is absolute, contains the /webhook/ path, and that the API and UI agree.
        var (tokenId, apiWebhookUrl) = await CreateTokenViaApiAsync("base-url-regression-test");

        Assert.True(
            Uri.IsWellFormedUriString(apiWebhookUrl, UriKind.Absolute),
            $"Expected an absolute webhook URL from the API, got: {apiWebhookUrl}");
        Assert.Contains("/webhook/", apiWebhookUrl);
        Assert.DoesNotContain("localhost:5000", apiWebhookUrl);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });
            var displayedUrl = (await page.InnerTextAsync("code.webhook-url")).Trim();
            Assert.Equal(apiWebhookUrl, displayedUrl);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task TokenDetail_IncomingRequest_AppearsInList()
    {
        var (tokenId, webhookUrl) = await CreateTokenViaApiAsync("incoming-request-test");

        // Send webhook before navigating so it is already in DB when page loads
        var content = new StringContent("{\"event\":\"e2e\"}", Encoding.UTF8, "application/json");
        await ApiClient.PostAsync(new Uri(webhookUrl).PathAndQuery, content);

        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
            await page.WaitForSelectorAsync(".request-row", new() { Timeout = 10_000 });
            var rows = await page.QuerySelectorAllAsync(".request-row");
            Assert.NotEmpty(rows);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task TokenDetail_DeleteToken_RedirectsToDashboard()
    {
        var (tokenId, _) = await CreateTokenViaApiAsync("delete-ui-test");
        var page = await NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/tokens/{tokenId}");
            await page.WaitForSelectorAsync("code.webhook-url", new() { Timeout = 10_000 });

            var deleteButton = page.GetByRole(AriaRole.Button,
                new() { NameRegex = new Regex("delete.*url", RegexOptions.IgnoreCase) });
            await deleteButton.ClickAsync(new LocatorClickOptions { Force = true });

            // Scope to the confirm modal — the toolbar also has a .btn-danger ("Delete URL")
            var confirmButton = page.Locator(".modal-panel .btn-danger");
            await confirmButton.WaitForAsync(new() { Timeout = 5_000 });
            await confirmButton.ClickAsync();

            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard"),
                new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        }
        finally { await page.CloseAsync(); }
    }
}

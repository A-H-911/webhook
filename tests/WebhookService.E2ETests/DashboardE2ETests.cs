using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebhookService.E2ETests;

/// <summary>
/// Requires the full stack running (docker compose up or local dev).
/// Set E2E_BASE_URL env var to target a running instance (default: http://localhost).
/// Before first run: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
public sealed class DashboardE2ETests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost";

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    private async Task<IPage> NewPageAsync()
    {
        var context = await _browser.NewContextAsync();
        return await context.NewPageAsync();
    }

    [Fact]
    public async Task Dashboard_LoadsWithHeading()
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
    public async Task Dashboard_CreateWebhookUrl_AppearsInList()
    {
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");

        await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
        await page.WaitForSelectorAsync("mat-dialog-container");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await page.WaitForSelectorAsync("mat-card");

        var cards = await page.QuerySelectorAllAsync("mat-card");
        Assert.NotEmpty(cards);
    }

    [Fact]
    public async Task Dashboard_CardClick_NavigatesToTokenDetail()
    {
        var page = await NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");

        await page.GetByRole(AriaRole.Button, new() { Name = "New URL" }).ClickAsync();
        await page.WaitForSelectorAsync("mat-dialog-container");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await page.WaitForSelectorAsync("mat-card");

        await page.ClickAsync("mat-card");
        await page.WaitForURLAsync(new Regex("/tokens/"));

        Assert.Contains("/tokens/", page.Url);
    }
}

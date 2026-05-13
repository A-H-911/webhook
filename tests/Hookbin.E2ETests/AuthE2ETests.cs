using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Hookbin.E2ETests;

/// <summary>
/// E2E coverage for authentication edge cases. Verifies the DANGER ZONE invariants:
/// - 401 stays on /login (no redirect loop because the interceptor excludes /api/auth/).
/// - Successful login lands on /dashboard.
/// - Unauthenticated access to a protected route redirects to /login.
/// Re-uses DashboardE2EFixture so the suite shares one authenticated session.
/// </summary>
[Collection("ComprehensiveE2E")]
public sealed class AuthE2ETests(DashboardE2EFixture fixture)
{
    private static string BaseUrl => DashboardE2EFixture.BaseUrl;
    private static string AuthUsername => DashboardE2EFixture.AuthUsername;
    private static string AuthPassword => DashboardE2EFixture.AuthPassword;

    [Fact]
    public async Task Login_WithBadPassword_StaysOnLoginPage_AndShowsError()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/login");
            await page.FillAsync("[data-testid='username']", AuthUsername);
            await page.FillAsync("[data-testid='password']", "definitely-not-the-password");
            await page.ClickAsync("[data-testid='login-submit']");

            // Give the SPA a beat to render the error
            await page.WaitForTimeoutAsync(500);

            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login"),
                new() { Timeout = 5_000 });

            // The error message should be visible on the page
            var bodyText = await page.InnerTextAsync("body");
            Assert.Contains("Invalid", bodyText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await page.CloseAsync();
            await context.DisposeAsync();
        }
    }

    // Note: a positive "correct-credentials redirects to /dashboard" test would add a
    // 4th hit on the /api/auth/login rate-limiter (5/min) on top of the fixture's
    // implicit login and the bad-password test in this class. The fixture's
    // LoginAsync already proves the happy path — duplicating it here is net-negative.

    [Fact]
    public async Task Login_WithEmptyFields_KeepsSubmitButtonDisabled()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}/login");

            var submit = page.Locator("[data-testid='login-submit']");
            await Assertions.Expect(submit).ToBeDisabledAsync();

            await page.FillAsync("[data-testid='username']", AuthUsername);
            // Password still empty — submit should remain disabled
            await Assertions.Expect(submit).ToBeDisabledAsync();
        }
        finally
        {
            await page.CloseAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnauthenticatedAccess_ToTokenDetailRoute_RedirectsToLogin()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        try
        {
            // Random GUID — even if it existed, the route is auth-guarded
            await page.GotoAsync($"{BaseUrl}/tokens/{Guid.NewGuid()}");

            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login"),
                new() { Timeout = 5_000 });
        }
        finally
        {
            await page.CloseAsync();
            await context.DisposeAsync();
        }
    }
}

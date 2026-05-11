using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hookbin.IntegrationTests;

public sealed class AuthApiTests(AuthWebAppFactory factory) : IClassFixture<AuthWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    [Fact]
    public async Task Login_WithValidCredentials_Returns200_AndSetsCookie()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = AuthWebAppFactory.TestPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Set-Cookie");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = "wrong-password",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithWrongUsername_Returns401()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "not-the-admin",
            password = AuthWebAppFactory.TestPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyCredentials_Returns400()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "",
            password = "",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTokens_WithoutCookie_Returns401()
    {
        var client = NewClient();
        var response = await client.GetAsync("/api/tokens");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTokens_AfterLogin_Returns200()
    {
        var client = NewClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = AuthWebAppFactory.TestPassword,
        });
        loginResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/tokens");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_WithValidCookie_ReturnsUsername()
    {
        var client = NewClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = AuthWebAppFactory.TestPassword,
        });
        loginResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("username").GetString().Should().Be(AuthWebAppFactory.TestUsername);
    }

    [Fact]
    public async Task Me_WithoutCookie_Returns401()
    {
        var client = NewClient();
        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithValidCookie_Returns200()
    {
        var client = NewClient();

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = AuthWebAppFactory.TestPassword,
        });

        var response = await client.PostAsJsonAsync("/api/auth/logout", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_ThenProtectedEndpoint_Returns401()
    {
        var client = NewClient();

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = AuthWebAppFactory.TestUsername,
            password = AuthWebAppFactory.TestPassword,
        });
        await client.PostAsJsonAsync("/api/auth/logout", new { });

        var response = await client.GetAsync("/api/tokens");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WebhookReceiver_WithoutCookie_IsNotUnauthorized()
    {
        var client = NewClient();
        var response = await client.PostAsync($"/webhook/{Guid.NewGuid()}",
            new StringContent("test-body"));

        // 404 (token not found) is expected — NOT 401
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_WithoutCookie_Returns200()
    {
        var client = NewClient();
        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_WithoutCookie_Returns200()
    {
        var client = NewClient();
        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

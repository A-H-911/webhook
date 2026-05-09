using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using WebhookService.API.Controllers;
using WebhookService.API.Options;
using WebhookService.API.Services;

namespace WebhookService.UnitTests.API.Controllers;

public sealed class AuthControllerTests
{
    private const string TestUsername = "testadmin";
    private const string TestPassword = "TestPassword1!";

    private readonly ISessionRevocationStore _revocationStore = Substitute.For<ISessionRevocationStore>();

    private AuthController CreateController(ClaimsPrincipal? user = null)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var authOptions = MicrosoftOptions.Create(new AuthOptions
        {
            Username = TestUsername,
            PasswordHash = passwordHash,
            SessionHours = 8
        });

        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = new ServiceCollection()
            .AddSingleton(authService)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        if (user is not null)
            httpContext.User = user;

        var controller = new AuthController(authOptions, _revocationStore, NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    // ── Login — validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ReturnsBadRequest_WhenUsernameIsEmpty()
    {
        var result = await CreateController().Login(new LoginRequest("", TestPassword));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_ReturnsBadRequest_WhenPasswordIsEmpty()
    {
        var result = await CreateController().Login(new LoginRequest(TestUsername, ""));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_ReturnsBadRequest_WhenBothFieldsEmpty()
    {
        var result = await CreateController().Login(new LoginRequest("", ""));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Login — wrong credentials ─────────────────────────────────────────────

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUsernameIsWrong()
    {
        var result = await CreateController().Login(new LoginRequest("wronguser", TestPassword));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenPasswordIsWrong()
    {
        var result = await CreateController().Login(new LoginRequest(TestUsername, "wrongpassword"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ── Login — success ───────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ReturnsOk_WhenCredentialsAreValid()
    {
        var result = await CreateController().Login(new LoginRequest(TestUsername, TestPassword));

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Login_ReturnsUsername_InOkPayload()
    {
        var result = await CreateController().Login(new LoginRequest(TestUsername, TestPassword));

        var value = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        value.GetType().GetProperty("username")!.GetValue(value).Should().Be(TestUsername);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesSession_WhenUserHasSid()
    {
        var sid = "test-sid-abc";
        var identity = new ClaimsIdentity(new[] { new Claim("sid", sid) }, "test");
        var controller = CreateController(new ClaimsPrincipal(identity));

        await controller.Logout();

        await _revocationStore.Received(1).RevokeAsync(sid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_DoesNotCallRevoke_WhenUserHasNoSid()
    {
        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "test");
        var controller = CreateController(new ClaimsPrincipal(identity));

        await controller.Logout();

        await _revocationStore.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_ReturnsOk()
    {
        var result = await CreateController().Logout();

        result.Should().BeOfType<OkResult>();
    }

    // ── Me ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Me_ReturnsUsername_WhenAuthenticated()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, TestUsername) }, "test");
        var controller = CreateController(new ClaimsPrincipal(identity));

        var result = controller.Me();

        var value = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        value.GetType().GetProperty("username")!.GetValue(value).Should().Be(TestUsername);
    }

    [Fact]
    public void Me_ReturnsNullUsername_WhenNotAuthenticated()
    {
        var result = CreateController().Me();

        var value = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        value.GetType().GetProperty("username")!.GetValue(value).Should().BeNull();
    }
}

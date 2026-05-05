using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WebhookService.API.Options;

namespace WebhookService.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IOptions<AuthOptions> authOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            return BadRequest(new { error = "Username and password are required." });

        var opts = authOptions.Value;
        var usernameMatch = string.Equals(body.Username, opts.Username, StringComparison.Ordinal);

        // Always run BCrypt.Verify to prevent timing attacks that reveal which field is wrong
        var passwordMatch = BCrypt.Net.BCrypt.Verify(body.Password, opts.PasswordHash);

        if (!usernameMatch || !passwordMatch)
        {
            logger.LogWarning("Failed login attempt");
            return Unauthorized(new { error = "Invalid credentials." });
        }

        var claims = new[] { new Claim(ClaimTypes.Name, opts.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Ok(new { username = opts.Username });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("me")]
    // Protected by the global fallback policy — no [AllowAnonymous] here.
    // A class-level [AllowAnonymous] would silently override this, so it is intentionally absent.
    public IActionResult Me()
    {
        var username = User.Identity?.Name;
        return Ok(new { username });
    }
}

public sealed record LoginRequest(string Username, string Password);

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Replaces real IAntiforgery in integration tests so every request is treated as
/// CSRF-valid regardless of headers. Without this, [AutoValidateAntiforgeryToken]
/// on controllers rejects every test POST that lacks an X-XSRF-TOKEN header.
/// </summary>
internal sealed class NoOpAntiforgery : IAntiforgery
{
    private static readonly AntiforgeryTokenSet Tokens =
        new("test-request-token", "test-cookie-token", "XSRF-TOKEN", "X-XSRF-TOKEN");

    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => Tokens;
    public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => Tokens;
    public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
    public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Hookbin.Application.Caching;
using Hookbin.Application.GeoIp;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;
using Hookbin.Domain.Services;

namespace Hookbin.API.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class WebhookController(
    IWebhookTokenRepository tokenRepository,
    IRequestQueuePublisher queuePublisher,
    ITokenCache tokenCache,
    IGeoIpService geoIpService,
    ILogger<WebhookController> logger) : ControllerBase
{
    [Route("webhook/{token:guid}")]
    [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete, HttpHead, HttpOptions]
    [EnableRateLimiting("webhook-receiver")]
    public async Task<IActionResult> Receive(Guid token, CancellationToken ct)
    {
        var webhookToken = await ResolveTokenAsync(token, ct);
        if (webhookToken is null)
            return NotFound();

        Request.EnableBuffering();
        Request.Body.Seek(0, SeekOrigin.Begin);
        var (body, isBodyBase64, bodyBytes) = await ReadBodyAsync(token, ct);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipCountry = geoIpService.GetCountry(ipAddress);
        var responseStatusCode = ComputeResponseStatusCode(webhookToken);

        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = webhookToken.Id,
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = Request.Method,
            Path = Request.Path.Value ?? "/",
            QueryString = Request.QueryString.HasValue ? Request.QueryString.Value : null,
            Headers = SerializeHeaders(Request.Headers),
            Body = body,
            IsBodyBase64 = isBodyBase64,
            ContentType = Request.ContentType,
            IpAddress = ipAddress,
            UserAgent = Request.Headers.UserAgent.ToString(),
            SizeBytes = Request.ContentLength ?? bodyBytes ?? 0,
            IpCountry = ipCountry,
            ResponseStatusCode = responseStatusCode
        };

        // Publish to Redis Stream — Worker persists to DB and fires SSE asynchronously.
        // On Redis failure after retries, RedisStreamPublisher throws; middleware returns 503.
        await queuePublisher.PublishAsync(request, ct);

        // Inactive tokens: queued for audit, but return 410 Gone to signal senders to stop.
        if (!webhookToken.IsActive)
            return StatusCode(StatusCodes.Status410Gone, new { message = "This webhook URL has been deactivated." });

        var custom = webhookToken.CustomResponse;
        if (custom is not null)
        {
            Response.StatusCode = custom.StatusCode;
            if (!string.IsNullOrWhiteSpace(custom.Headers))
            {
                try
                {
                    var extraHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(custom.Headers);
                    if (extraHeaders is not null)
                        foreach (var (key, value) in extraHeaders)
                            Response.Headers.TryAdd(key, value);
                }
                catch (JsonException) { }
            }
            return custom.Body is not null
                ? Content(custom.Body, custom.ContentType)
                : new EmptyResult();
        }

        return Ok(new { message = "Webhook received." });
    }

    private async Task<WebhookToken?> ResolveTokenAsync(Guid token, CancellationToken ct)
    {
        var cached = await tokenCache.GetAsync(token, ct);
        if (cached is not null)
            return cached;

        var found = await tokenRepository.GetByTokenIncludingInactiveAsync(token, ct);
        if (found is not null)
            await tokenCache.SetAsync(token, found, ct);

        return found;
    }

    private async Task<(string? Body, bool IsBase64, long? ByteCount)> ReadBodyAsync(Guid token, CancellationToken ct)
    {
        if (Request.ContentLength == 0)
            return (null, false, 0);

        // The global antiforgery filter reads Request.Form for form-encoded content before
        // this controller runs, exhausting the body stream. ReadFormAsync returns the cached
        // parsed form regardless of stream position.
        if (Request.HasFormContentType)
        {
            try
            {
                var form = await Request.ReadFormAsync(ct);
                if (form.Count == 0)
                    return (null, false, 0);

                var encoded = string.Join("&", form.Select(f =>
                    $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(string.Join(",", f.Value.ToArray()))}"));
                var byteLen = (long)Encoding.UTF8.GetByteCount(encoded);
                return (encoded, false, byteLen);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to read form for token {Token}", token);
                throw new BadHttpRequestException("Could not read request body.", StatusCodes.Status400BadRequest);
            }
        }

        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            if (bytes.Length == 0)
                return (null, false, 0);

            try
            {
                var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes);
                return (text, false, bytes.LongLength);
            }
            catch (DecoderFallbackException)
            {
                return (Convert.ToBase64String(bytes), true, bytes.LongLength);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read request body for token {Token}", token);
            throw new BadHttpRequestException("Could not read request body.", StatusCodes.Status400BadRequest);
        }
    }

    private static int ComputeResponseStatusCode(WebhookToken token)
    {
        if (!token.IsActive)
            return StatusCodes.Status410Gone;
        if (token.CustomResponse is not null)
            return token.CustomResponse.StatusCode;
        return StatusCodes.Status200OK;
    }

    // Values are string[] so multi-value headers (e.g. Set-Cookie) round-trip losslessly.
    // JSON shape: {"K":["v1","v2"]} — always arrays, never plain strings.
    private static string SerializeHeaders(IHeaderDictionary headers)
    {
        var dict = headers
            .Where(h => !h.Key.StartsWith(':'))
            .ToDictionary(h => h.Key, h => h.Value.ToArray());
        return JsonSerializer.Serialize(dict);
    }
}

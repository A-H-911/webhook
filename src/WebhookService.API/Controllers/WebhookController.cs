using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.API.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class WebhookController(
    IWebhookTokenRepository tokenRepository,
    IWebhookRequestRepository requestRepository,
    ISseNotifier sseNotifier,
    IMemoryCache cache,
    ILogger<WebhookController> logger) : ControllerBase
{
    private static readonly MemoryCacheEntryOptions TokenCacheOptions =
        new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(5));

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
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = Request.Headers.UserAgent.ToString(),
            SizeBytes = Request.ContentLength ?? bodyBytes ?? 0
        };

        await requestRepository.AddAsync(request, ct);

        // Inactive tokens: persisted for audit, but return 410 Gone to signal senders to stop.
        if (!webhookToken.IsActive)
            return StatusCode(StatusCodes.Status410Gone, new { message = "This webhook URL has been deactivated." });

        try
        {
            var summary = JsonSerializer.Serialize(new
            {
                id = request.Id,
                method = request.Method,
                path = request.Path,
                receivedAt = request.ReceivedAt,
                sizeBytes = request.SizeBytes,
                ipAddress = request.IpAddress,
                contentType = request.ContentType,
                statusCode = 200
            });
            await sseNotifier.NotifyAsync(webhookToken.Id, summary);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SSE notify failed for token {TokenId}", webhookToken.Id);
        }

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
        var cacheKey = $"token:{token}";
        if (cache.TryGetValue(cacheKey, out WebhookToken? cached))
            return cached;

        var found = await tokenRepository.GetByTokenIncludingInactiveAsync(token, ct);
        if (found is not null)
            cache.Set(cacheKey, found, TokenCacheOptions);

        return found;
    }

    private async Task<(string? Body, bool IsBase64, long? ByteCount)> ReadBodyAsync(Guid token, CancellationToken ct)
    {
        if (Request.ContentLength == 0)
            return (null, false, 0);

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

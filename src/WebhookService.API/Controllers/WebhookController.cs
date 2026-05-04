using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.API.Controllers;

[ApiController]
public sealed class WebhookController(
    IWebhookTokenRepository tokenRepository,
    IWebhookRequestRepository requestRepository,
    ISseNotifier sseNotifier,
    IMemoryCache cache,
    ILogger<WebhookController> logger) : ControllerBase
{
    private static readonly MemoryCacheEntryOptions TokenCacheOptions =
        new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(5));

    [Route("hooks/{token:guid}")]
    [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete, HttpHead, HttpOptions]
    public async Task<IActionResult> Receive(Guid token, CancellationToken ct)
    {
        var webhookToken = await ResolveTokenAsync(token, ct);
        if (webhookToken is null)
            return NotFound();

        Request.EnableBuffering();
        var body = await ReadBodyAsync(ct);

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
            IsBodyBase64 = false,
            ContentType = Request.ContentType,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = Request.Headers.UserAgent.ToString(),
            SizeBytes = Request.ContentLength ?? body?.Length ?? 0
        };

        await requestRepository.AddAsync(request, ct);

        try
        {
            var summary = JsonSerializer.Serialize(new
            {
                id = request.Id,
                method = request.Method,
                path = request.Path,
                receivedAt = request.ReceivedAt
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

        var found = await tokenRepository.GetByTokenAsync(token, ct);
        if (found is not null)
            cache.Set(cacheKey, found, TokenCacheOptions);

        return found;
    }

    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength == 0)
            return null;

        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        return string.IsNullOrEmpty(body) ? null : body;
    }

    private static string SerializeHeaders(IHeaderDictionary headers)
    {
        var dict = headers
            .Where(h => !h.Key.StartsWith(':'))
            .ToDictionary(h => h.Key, h => h.Value.ToString());
        return JsonSerializer.Serialize(dict);
    }
}

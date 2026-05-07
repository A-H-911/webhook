using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.API.Controllers;

[ApiController]
public sealed class SseController(ISseNotifier sseNotifier, IWebhookTokenRepository tokenRepository) : ControllerBase
{
    [HttpGet("api/tokens/{tokenId:guid}/sse")]
    public async Task Subscribe(Guid tokenId, CancellationToken ct)
    {
        var token = await tokenRepository.GetByIdAsync(tokenId, ct);
        if (token is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!sseNotifier.TrySubscribe(tokenId))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        try
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            var retryFrame = Encoding.UTF8.GetBytes("retry: 5000\n\n");
            await Response.Body.WriteAsync(retryFrame, ct);
            await Response.Body.FlushAsync(ct);

            await foreach (var evt in sseNotifier.SubscribeAsync(tokenId, ct))
            {
                var line = $"event: {evt.EventName}\ndata: {evt.Data}\n\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                await Response.Body.WriteAsync(bytes, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        finally
        {
            sseNotifier.Unsubscribe(tokenId);
        }
    }
}

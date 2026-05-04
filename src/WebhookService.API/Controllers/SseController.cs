using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebhookService.Domain.Services;

namespace WebhookService.API.Controllers;

[ApiController]
public sealed class SseController(ISseNotifier sseNotifier) : ControllerBase
{
    [HttpGet("api/tokens/{tokenId:guid}/sse")]
    public async Task Subscribe(Guid tokenId, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await foreach (var evt in sseNotifier.SubscribeAsync(tokenId, ct))
        {
            var line = $"event: {evt.EventName}\ndata: {evt.Data}\n\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

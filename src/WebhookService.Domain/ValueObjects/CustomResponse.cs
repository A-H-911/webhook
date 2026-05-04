namespace WebhookService.Domain.ValueObjects;

public sealed class CustomResponse
{
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "text/plain";
    public string? Body { get; init; }
    public string Headers { get; init; } = "{}";
}

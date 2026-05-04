namespace WebhookService.Domain.Services;

public sealed record SseEvent(string EventName, string Data);

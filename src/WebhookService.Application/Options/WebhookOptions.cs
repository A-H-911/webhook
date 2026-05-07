namespace WebhookService.Application.Options;

public sealed class WebhookOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public int RetentionDays { get; init; } = 7;
    public int MaxRequestSizeMb { get; init; } = 5;
    // Max requests per second per token on the webhook receiver. Override via WEBHOOK__ReceiverRateLimitPerSecond.
    public int ReceiverRateLimitPerSecond { get; init; } = 250;
}

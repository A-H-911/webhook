namespace WebhookService.Application.Options;

public sealed class WebhookOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public int RetentionDays { get; init; } = 7;
    public int MaxRequestSizeMb { get; init; } = 5;
}

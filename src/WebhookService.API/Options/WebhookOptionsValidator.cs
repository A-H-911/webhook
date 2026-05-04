using Microsoft.Extensions.Options;

namespace WebhookService.API.Options;

public sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return ValidateOptionsResult.Fail(
                "Webhook:BaseUrl is required. Set the WEBHOOK_BASE_URL environment variable.");

        if (options.MaxRequestSizeMb < 1 || options.MaxRequestSizeMb > 100)
            return ValidateOptionsResult.Fail(
                "Webhook:MaxRequestSizeMb must be between 1 and 100.");

        if (options.RetentionDays < 0 || options.RetentionDays > 365)
            return ValidateOptionsResult.Fail(
                "Webhook:RetentionDays must be between 0 and 365.");

        return ValidateOptionsResult.Success;
    }
}

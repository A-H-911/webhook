using System.Text.Json;
using FluentValidation;

namespace WebhookService.Application.Tokens.Commands.SetCustomResponse;

public sealed class SetCustomResponseCommandValidator : AbstractValidator<SetCustomResponseCommand>
{
    public SetCustomResponseCommandValidator()
    {
        RuleFor(x => x.StatusCode)
            .Must(code => (code >= 200 && code <= 299) || (code >= 400 && code <= 599))
            .WithMessage("StatusCode must be 2xx, 4xx, or 5xx. Redirect (3xx) codes are not allowed.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.Headers)
            .NotNull()
            .Must(BeValidJsonObject)
            .WithMessage("Headers must be a valid JSON object.");
    }

    private static bool BeValidJsonObject(string? value)
    {
        if (value is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}

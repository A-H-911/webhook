using FluentValidation;

namespace WebhookService.Application.Tokens.Commands.UpdateToken;

public sealed class UpdateTokenCommandValidator : AbstractValidator<UpdateTokenCommand>
{
    public UpdateTokenCommandValidator()
    {
        RuleFor(x => x.Description)
            .MaximumLength(200)
            .When(x => x.Description is not null);
    }
}

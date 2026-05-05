using FluentValidation;

namespace WebhookService.Application.Tokens.Commands.CreateToken;

public sealed class CreateTokenCommandValidator : AbstractValidator<CreateTokenCommand>
{
    public CreateTokenCommandValidator()
    {
        RuleFor(x => x.Description)
            .MaximumLength(200)
            .When(x => x.Description is not null);
    }
}

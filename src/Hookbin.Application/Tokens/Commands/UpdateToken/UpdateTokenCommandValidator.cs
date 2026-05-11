using FluentValidation;

namespace Hookbin.Application.Tokens.Commands.UpdateToken;

public sealed class UpdateTokenCommandValidator : AbstractValidator<UpdateTokenCommand>
{
    public UpdateTokenCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(80).WithMessage("Name must not exceed 80 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(200)
            .When(x => x.Description is not null);
    }
}

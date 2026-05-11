using FluentValidation;

namespace Hookbin.Application.Requests.Commands.SetRequestNote;

public sealed class SetRequestNoteCommandValidator : AbstractValidator<SetRequestNoteCommand>
{
    public SetRequestNoteCommandValidator()
    {
        RuleFor(x => x.TokenId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

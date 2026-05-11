using MediatR;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Requests.Commands.SetRequestNote;

internal sealed class SetRequestNoteCommandHandler(IWebhookRequestRepository repository)
    : IRequestHandler<SetRequestNoteCommand, bool>
{
    public Task<bool> Handle(SetRequestNoteCommand command, CancellationToken cancellationToken)
        => repository.UpdateNoteAsync(command.RequestId, command.TokenId, command.Note, cancellationToken);
}

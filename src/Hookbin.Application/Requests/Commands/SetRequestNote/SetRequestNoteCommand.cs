using MediatR;

namespace Hookbin.Application.Requests.Commands.SetRequestNote;

public sealed record SetRequestNoteCommand(Guid TokenId, Guid RequestId, string? Note) : IRequest<bool>;

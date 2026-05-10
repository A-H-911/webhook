using MediatR;

namespace WebhookService.Application.Requests.Commands.SetRequestNote;

public sealed record SetRequestNoteCommand(Guid TokenId, Guid RequestId, string? Note) : IRequest<bool>;

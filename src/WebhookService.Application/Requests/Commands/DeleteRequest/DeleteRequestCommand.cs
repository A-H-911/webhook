using MediatR;

namespace WebhookService.Application.Requests.Commands.DeleteRequest;

public sealed record DeleteRequestCommand(Guid TokenId, Guid Id) : IRequest<bool>;

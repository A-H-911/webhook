using MediatR;

namespace WebhookService.Application.Tokens.Commands.DeleteToken;

public sealed record DeleteTokenCommand(Guid Id) : IRequest<bool>;

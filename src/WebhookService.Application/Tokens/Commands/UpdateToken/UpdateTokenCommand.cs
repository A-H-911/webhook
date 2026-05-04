using MediatR;
using WebhookService.Application.Tokens.Queries.GetToken;

namespace WebhookService.Application.Tokens.Commands.UpdateToken;

public sealed record UpdateTokenCommand(Guid Id, string? Description, bool IsActive) : IRequest<TokenDto?>;

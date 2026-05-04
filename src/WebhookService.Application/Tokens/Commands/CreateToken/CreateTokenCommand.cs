using MediatR;
using WebhookService.Application.Tokens.Queries.GetToken;

namespace WebhookService.Application.Tokens.Commands.CreateToken;

public sealed record CreateTokenCommand(string? Description) : IRequest<TokenDto>;

using MediatR;

namespace WebhookService.Application.Tokens.Queries.GetToken;

public sealed record GetTokenQuery(Guid Id) : IRequest<TokenDto?>;

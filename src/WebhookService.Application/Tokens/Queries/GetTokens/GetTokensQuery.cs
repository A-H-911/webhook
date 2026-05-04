using MediatR;
using WebhookService.Application.Tokens.Queries.GetToken;

namespace WebhookService.Application.Tokens.Queries.GetTokens;

public sealed record GetTokensQuery : IRequest<IReadOnlyList<TokenDto>>;

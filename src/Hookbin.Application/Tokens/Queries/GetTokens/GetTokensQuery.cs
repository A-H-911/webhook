using MediatR;

namespace Hookbin.Application.Tokens.Queries.GetTokens;

public sealed record GetTokensQuery(int Skip = 0, int Take = 50) : IRequest<TokensPagedResult>;

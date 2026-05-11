using MediatR;

namespace Hookbin.Application.Tokens.Queries.GetToken;

public sealed record GetTokenQuery(Guid Id) : IRequest<TokenDto?>;

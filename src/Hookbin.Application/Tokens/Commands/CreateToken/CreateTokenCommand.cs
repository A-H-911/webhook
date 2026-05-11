using MediatR;
using Hookbin.Application.Tokens.Queries.GetToken;

namespace Hookbin.Application.Tokens.Commands.CreateToken;

public sealed record CreateTokenCommand(string Name, string? Description) : IRequest<TokenDto>;

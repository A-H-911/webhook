using MediatR;
using Hookbin.Application.Tokens.Queries.GetToken;

namespace Hookbin.Application.Tokens.Commands.UpdateToken;

public sealed record UpdateTokenCommand(Guid Id, string Name, string? Description, bool IsActive) : IRequest<TokenDto?>;

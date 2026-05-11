using MediatR;

namespace Hookbin.Application.Tokens.Commands.DeleteToken;

public sealed record DeleteTokenCommand(Guid Id) : IRequest<bool>;

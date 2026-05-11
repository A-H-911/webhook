using MediatR;

namespace Hookbin.Application.Tokens.Commands.ResetCustomResponse;

public sealed record ResetCustomResponseCommand(Guid Id) : IRequest<bool>;

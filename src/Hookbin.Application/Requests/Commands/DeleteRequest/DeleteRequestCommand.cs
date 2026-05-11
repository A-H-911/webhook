using MediatR;

namespace Hookbin.Application.Requests.Commands.DeleteRequest;

public sealed record DeleteRequestCommand(Guid TokenId, Guid Id) : IRequest<bool>;

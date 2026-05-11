using MediatR;

namespace Hookbin.Application.Requests.Commands.ClearRequests;

public sealed record ClearRequestsCommand(Guid TokenId) : IRequest;

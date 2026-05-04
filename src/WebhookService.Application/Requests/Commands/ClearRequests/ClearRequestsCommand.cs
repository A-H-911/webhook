using MediatR;

namespace WebhookService.Application.Requests.Commands.ClearRequests;

public sealed record ClearRequestsCommand(Guid TokenId) : IRequest;

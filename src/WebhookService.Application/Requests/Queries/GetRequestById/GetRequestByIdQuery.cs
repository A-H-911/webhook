using MediatR;

namespace WebhookService.Application.Requests.Queries.GetRequestById;

public sealed record GetRequestByIdQuery(Guid TokenId, Guid Id) : IRequest<WebhookRequestDetailDto?>;

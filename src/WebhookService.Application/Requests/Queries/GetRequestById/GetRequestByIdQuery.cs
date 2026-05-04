using MediatR;

namespace WebhookService.Application.Requests.Queries.GetRequestById;

public sealed record GetRequestByIdQuery(Guid Id) : IRequest<WebhookRequestDetailDto?>;

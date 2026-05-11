using MediatR;

namespace Hookbin.Application.Requests.Queries.GetRequestById;

public sealed record GetRequestByIdQuery(Guid TokenId, Guid Id) : IRequest<WebhookRequestDetailDto?>;

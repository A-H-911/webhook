using MediatR;

namespace WebhookService.Application.Requests.Queries.GetRequests;

public sealed record GetRequestsQuery(
    Guid TokenId,
    int Page,
    int PageSize,
    string? Search) : IRequest<PagedResult<WebhookRequestSummaryDto>>;

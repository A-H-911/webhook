using MediatR;

namespace Hookbin.Application.Requests.Queries.GetRequests;

public sealed record GetRequestsQuery(
    Guid TokenId,
    int Page,
    int PageSize,
    string? Search,
    string[]? Methods = null,
    int[]? StatusGroups = null) : IRequest<PagedResult<WebhookRequestSummaryDto>>;

using MediatR;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Requests.Queries.GetRequests;

internal sealed class GetRequestsQueryHandler(IWebhookRequestRepository repository)
    : IRequestHandler<GetRequestsQuery, PagedResult<WebhookRequestSummaryDto>>
{
    public async Task<PagedResult<WebhookRequestSummaryDto>> Handle(GetRequestsQuery request, CancellationToken cancellationToken)
    {
        var (items, total) = await repository.GetPagedAsync(
            request.TokenId, request.Page, request.PageSize, request.Search, cancellationToken);

        var dtos = items.Select(ToSummary).ToList();
        return new PagedResult<WebhookRequestSummaryDto>(dtos, total, request.Page, request.PageSize);
    }

    private static WebhookRequestSummaryDto ToSummary(WebhookRequest r) => new(
        r.Id, r.TokenId, r.Method, r.Path, r.ReceivedAt, r.ContentType, r.SizeBytes, r.IpAddress);
}

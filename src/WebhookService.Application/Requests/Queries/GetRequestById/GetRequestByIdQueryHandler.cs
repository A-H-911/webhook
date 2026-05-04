using MediatR;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Requests.Queries.GetRequestById;

internal sealed class GetRequestByIdQueryHandler(IWebhookRequestRepository repository)
    : IRequestHandler<GetRequestByIdQuery, WebhookRequestDetailDto?>
{
    public async Task<WebhookRequestDetailDto?> Handle(GetRequestByIdQuery request, CancellationToken cancellationToken)
    {
        var r = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (r is null || r.TokenId != request.TokenId) return null;
        return ToDetail(r);
    }

    private static WebhookRequestDetailDto ToDetail(WebhookRequest r) => new(
        r.Id, r.TokenId, r.Method, r.Path, r.QueryString,
        r.ReceivedAt, r.ContentType, r.Headers, r.Body,
        r.IsBodyBase64, r.SizeBytes, r.IpAddress, r.UserAgent);
}

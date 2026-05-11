using System.Text.Json;
using MediatR;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Requests.Queries.ExportRequest;

internal sealed class ExportRequestQueryHandler(IWebhookRequestRepository repository)
    : IRequestHandler<ExportRequestQuery, byte[]?>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<byte[]?> Handle(ExportRequestQuery request, CancellationToken cancellationToken)
    {
        var r = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (r is null || r.TokenId != request.TokenId) return null;
        return JsonSerializer.SerializeToUtf8Bytes(ToExport(r), JsonOptions);
    }

    private static object ToExport(WebhookRequest r) => new
    {
        r.Id, r.TokenId, r.Method, r.Path, r.QueryString,
        r.ReceivedAt, r.ContentType, r.Headers, r.Body,
        r.IsBodyBase64, r.SizeBytes, r.IpAddress, r.UserAgent
    };
}

using MediatR;

namespace WebhookService.Application.Requests.Queries.ExportRequest;

public sealed record ExportRequestQuery(Guid Id) : IRequest<byte[]?>;

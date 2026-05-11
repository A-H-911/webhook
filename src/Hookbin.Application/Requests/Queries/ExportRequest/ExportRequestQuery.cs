using MediatR;

namespace Hookbin.Application.Requests.Queries.ExportRequest;

public sealed record ExportRequestQuery(Guid TokenId, Guid Id) : IRequest<byte[]?>;

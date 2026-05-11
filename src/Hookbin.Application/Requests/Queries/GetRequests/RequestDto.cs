namespace Hookbin.Application.Requests.Queries.GetRequests;

public sealed record WebhookRequestSummaryDto(
    Guid Id,
    Guid TokenId,
    string Method,
    string Path,
    DateTimeOffset ReceivedAt,
    string? ContentType,
    long SizeBytes,
    string IpAddress,
    int? ResponseStatusCode,
    string? IpCountry);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

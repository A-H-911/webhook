namespace Hookbin.Application.Requests.Queries.GetRequestById;

public sealed record WebhookRequestDetailDto(
    Guid Id,
    Guid TokenId,
    string Method,
    string Path,
    string? QueryString,
    DateTimeOffset ReceivedAt,
    string? ContentType,
    string Headers,
    string? Body,
    bool IsBodyBase64,
    long SizeBytes,
    string IpAddress,
    string? UserAgent,
    long? ProcessingTimeMs,
    string? Note,
    int? ResponseStatusCode,
    string? IpCountry);

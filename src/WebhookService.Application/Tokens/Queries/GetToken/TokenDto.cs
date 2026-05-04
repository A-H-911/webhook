namespace WebhookService.Application.Tokens.Queries.GetToken;

public sealed record TokenDto(
    Guid Id,
    Guid Token,
    string WebhookUrl,
    string? Description,
    DateTimeOffset CreatedAt,
    bool IsActive,
    CustomResponseDto? CustomResponse);

public sealed record CustomResponseDto(
    int StatusCode,
    string ContentType,
    string? Body,
    string Headers);

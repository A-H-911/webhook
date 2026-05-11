namespace Hookbin.Application.Tokens.Queries.GetTokens;

public sealed record TokenListItemDto(
    Guid Id,
    Guid Token,
    string Name,
    string WebhookUrl,
    string? Description,
    DateTimeOffset CreatedAt,
    bool IsActive,
    bool HasCustomResponse,
    int LifetimeRequestCount,
    int RequestCount24h,
    int[] Sparkline24h,
    DateTimeOffset? LastReceivedAt);

public sealed record TokensPagedResult(
    IReadOnlyList<TokenListItemDto> Items,
    int Total,
    bool HasMore);

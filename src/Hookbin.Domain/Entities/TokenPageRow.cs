namespace Hookbin.Domain.Entities;

public sealed record TokenPageRow(
    WebhookToken Token,
    int LifetimeRequestCount,
    int RequestCount24h,
    DateTimeOffset? LastReceivedAt);

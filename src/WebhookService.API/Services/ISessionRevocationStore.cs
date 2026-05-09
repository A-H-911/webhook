namespace WebhookService.API.Services;

public interface ISessionRevocationStore
{
    Task RevokeAsync(string sessionId, CancellationToken ct = default);
    Task<bool> IsRevokedAsync(string sessionId, CancellationToken ct = default);
}

using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Hookbin.API.Options;

namespace Hookbin.API.Services;

internal sealed class RedisSessionRevocationStore(
    IConnectionMultiplexer redis,
    IOptions<AuthOptions> authOptions,
    ILogger<RedisSessionRevocationStore> logger) : ISessionRevocationStore
{
    private const string KeyPrefix = "wh:revoked-session:";

    private int TtlSeconds => (int)(authOptions.Value.SessionHours * 3600);

    public async Task RevokeAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync(
                $"{KeyPrefix}{sessionId}",
                "1",
                TimeSpan.FromSeconds(TtlSeconds));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            // Fail open — session is signed out locally even if Redis is down.
            logger.LogWarning(ex, "Redis session revocation SET failed for session {SessionId}", sessionId);
        }
    }

    public async Task<bool> IsRevokedAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            return await db.KeyExistsAsync($"{KeyPrefix}{sessionId}");
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            // Fail open — allow the request rather than blocking all sessions when Redis is down.
            logger.LogWarning(ex, "Redis session revocation EXISTS check failed for session {SessionId}", sessionId);
            return false;
        }
    }
}

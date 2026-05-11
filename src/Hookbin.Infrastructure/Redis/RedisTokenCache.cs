using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Hookbin.Application.Caching;
using Hookbin.Domain.Entities;

namespace Hookbin.Infrastructure.Redis;

internal sealed class RedisTokenCache(
    IConnectionMultiplexer redis,
    ILogger<RedisTokenCache> logger) : ITokenCache
{
    private const string KeyPrefix = "wh:token:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<WebhookToken?> GetAsync(Guid tokenGuid, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(tokenGuid);
            var value = await db.StringGetAsync(key);
            if (!value.HasValue)
                return null;

            var token = JsonSerializer.Deserialize<WebhookToken>(value.ToString());
            if (token is not null)
                await db.KeyExpireAsync(key, Ttl); // sliding expiration

            return token;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis cache GET failed for token {TokenGuid}; falling back to database", tokenGuid);
            return null;
        }
    }

    public async Task SetAsync(Guid tokenGuid, WebhookToken token, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(token);
            await db.StringSetAsync(BuildKey(tokenGuid), json, Ttl);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis cache SET failed for token {TokenGuid}", tokenGuid);
        }
    }

    public async Task RemoveAsync(Guid tokenGuid, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(tokenGuid));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis cache DEL failed for token {TokenGuid}", tokenGuid);
        }
    }

    private static string BuildKey(Guid tokenGuid) => $"{KeyPrefix}{tokenGuid}";
}

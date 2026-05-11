using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Hookbin.Domain.Entities;
using Hookbin.Infrastructure.Redis;

namespace Hookbin.UnitTests.Infrastructure.Redis;

public sealed class RedisTokenCacheTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();

    private RedisTokenCache CreateCache()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        return new RedisTokenCache(_redis, NullLogger<RedisTokenCache>.Instance);
    }

    private static WebhookToken MakeToken() => new()
    {
        Id = Guid.NewGuid(),
        Token = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotInRedis()
    {
        // Arrange
        var cache = CreateCache();
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        // Act
        var result = await cache.GetAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsDeserializedToken_WhenFoundInRedis()
    {
        // Arrange
        var cache = CreateCache();
        var token = MakeToken();
        var json = System.Text.Json.JsonSerializer.Serialize(token);
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));
        _db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        var result = await cache.GetAsync(token.Token);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(token.Id);
        result.Token.Should().Be(token.Token);
    }

    [Fact]
    public async Task GetAsync_ResetsSlipingExpiry_WhenTokenFound()
    {
        // Arrange
        var cache = CreateCache();
        var token = MakeToken();
        var json = System.Text.Json.JsonSerializer.Serialize(token);
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));
        _db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        await cache.GetAsync(token.Token);

        // Assert — sliding expiration resets TTL on each hit
        await _db.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRedisThrows()
    {
        // Arrange
        var cache = CreateCache();
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "down"));

        // Act
        var result = await cache.GetAsync(Guid.NewGuid());

        // Assert — Redis failure is non-fatal; null causes DB fallback in controller
        result.Should().BeNull();
    }

    // ── SetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_CallsStringSet_WithCorrectKeyAndTtl()
    {
        // Arrange
        var cache = CreateCache();
        var token = MakeToken();
        _db.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<When>(),
                Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        await cache.SetAsync(token.Token, token);

        // Assert
        await _db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => (k.ToString() ?? "").Contains(token.Token.ToString())),
            Arg.Any<RedisValue>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow_WhenRedisThrows()
    {
        // Arrange
        var cache = CreateCache();
        _db.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<When>(),
                Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "down"));

        // Act
        var act = () => cache.SetAsync(Guid.NewGuid(), MakeToken());

        // Assert — Redis failure on set is non-fatal
        await act.Should().NotThrowAsync();
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_CallsKeyDelete_WithCorrectKey()
    {
        // Arrange
        var cache = CreateCache();
        var tokenGuid = Guid.NewGuid();
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        await cache.RemoveAsync(tokenGuid);

        // Assert
        await _db.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey>(k => (k.ToString() ?? "").Contains(tokenGuid.ToString())),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenRedisThrows()
    {
        // Arrange
        var cache = CreateCache();
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "down"));

        // Act
        var act = () => cache.RemoveAsync(Guid.NewGuid());

        // Assert
        await act.Should().NotThrowAsync();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Hookbin.API.Options;
using Hookbin.API.Services;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace Hookbin.UnitTests.Infrastructure.Redis;

public sealed class RedisSessionRevocationStoreTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();

    private RedisSessionRevocationStore CreateStore(int sessionHours = 8)
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        var options = MicrosoftOptions.Create(new AuthOptions
        {
            Username = "admin",
            PasswordHash = "$2a$12$fakehashfortestingpurposesXXXXXX",
            SessionHours = sessionHours
        });
        return new RedisSessionRevocationStore(_redis, options, NullLogger<RedisSessionRevocationStore>.Instance);
    }

    // ── RevokeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_CallsStringSet_WithKeyContainingSid()
    {
        // Arrange
        var sid = "test-session-id";
        var store = CreateStore();
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        await store.RevokeAsync(sid);

        // Assert
        await _db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString()!.Contains(sid)),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeAsync_CallsStringSet_WithTtlMatchingSessionHours()
    {
        // Arrange
        var store = CreateStore(sessionHours: 8);
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);
        var expectedTtl = TimeSpan.FromSeconds(8 * 3600);

        // Act
        await store.RevokeAsync("any-sid");

        // Assert
        await _db.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Is<TimeSpan?>(t => t == expectedTtl),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeAsync_DoesNotThrow_WhenRedisThrows()
    {
        // Arrange
        var store = CreateStore();
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "down"));

        // Act
        var act = () => store.RevokeAsync("any-sid");

        // Assert — Redis failure is non-fatal; session is signed out locally
        await act.Should().NotThrowAsync();
    }

    // ── IsRevokedAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task IsRevokedAsync_ReturnsTrue_WhenKeyExistsInRedis()
    {
        // Arrange
        var store = CreateStore();
        _db.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        // Act
        var result = await store.IsRevokedAsync("any-sid");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRevokedAsync_ReturnsFalse_WhenKeyMissingInRedis()
    {
        // Arrange
        var store = CreateStore();
        _db.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        // Act
        var result = await store.IsRevokedAsync("any-sid");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_ReturnsFalse_WhenRedisThrows()
    {
        // Arrange
        var store = CreateStore();
        _db.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "down"));

        // Act
        var result = await store.IsRevokedAsync("any-sid");

        // Assert — fail open; do not block all sessions when Redis is unavailable
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_ChecksKeyContainingSid()
    {
        // Arrange
        var sid = "lookup-sid-abc";
        var store = CreateStore();
        _db.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        // Act
        await store.IsRevokedAsync(sid);

        // Assert — key must embed the sid for correct Redis isolation
        await _db.Received(1).KeyExistsAsync(
            Arg.Is<RedisKey>(k => k.ToString()!.Contains(sid)),
            Arg.Any<CommandFlags>());
    }
}

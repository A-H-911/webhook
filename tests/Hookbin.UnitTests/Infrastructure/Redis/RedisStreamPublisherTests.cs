using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Hookbin.Domain.Entities;
using Hookbin.Infrastructure.Redis;

namespace Hookbin.UnitTests.Infrastructure.Redis;

public sealed class RedisStreamPublisherTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();

    private RedisStreamPublisher CreatePublisher()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        return new RedisStreamPublisher(_redis);
    }

    private static WebhookRequest MakeRequest() => new()
    {
        Id = Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        ReceivedAt = DateTimeOffset.UtcNow,
        Method = "POST",
        Path = "/webhook/test",
        Headers = "{}",
        IpAddress = "127.0.0.1",
        UserAgent = string.Empty,
        SizeBytes = 0
    };

    [Fact]
    public async Task PublishAsync_CallsStreamAddAsync_WithCorrectKey()
    {
        // Arrange
        var publisher = CreatePublisher();
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisValue.EmptyString);

        // Act
        await publisher.PublishAsync(MakeRequest(), CancellationToken.None);

        // Assert — stream key must be the well-known constant
        await _db.Received(1).StreamAddAsync(
            Arg.Is<RedisKey>(k => k == (RedisKey)"webhook-requests"),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task PublishAsync_SetsMaxLength_ToApproximate10000()
    {
        // Arrange
        var publisher = CreatePublisher();
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisValue.EmptyString);

        // Act
        await publisher.PublishAsync(MakeRequest(), CancellationToken.None);

        // Assert — MAXLEN ~ 10000 keeps stream bounded
        await _db.Received(1).StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Is<int?>(max => max == 10_000),
            Arg.Is<bool>(approx => approx),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task PublishAsync_SerializesRequest_AsPayloadField()
    {
        // Arrange
        var publisher = CreatePublisher();
        NameValueEntry[]? capturedEntries = null;
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Do<NameValueEntry[]>(e => capturedEntries = e),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisValue.EmptyString);

        // Act
        await publisher.PublishAsync(MakeRequest(), CancellationToken.None);

        // Assert
        capturedEntries.Should().NotBeNull();
        capturedEntries!.Should().ContainSingle(e => e.Name == "payload");
        capturedEntries![0].Value.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_RetriesOnRedisConnectionException()
    {
        // Arrange
        var publisher = CreatePublisher();
        var callCount = 0;
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount < 3)
                    throw new RedisConnectionException(ConnectionFailureType.SocketClosed, "connection lost");
                return Task.FromResult(RedisValue.EmptyString);
            });

        // Act
        await publisher.PublishAsync(MakeRequest(), CancellationToken.None);

        // Assert — succeeded on third attempt
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task PublishAsync_ThrowsAfterAllRetriesExhausted()
    {
        // Arrange
        var publisher = CreatePublisher();
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketClosed, "always fails"));

        // Act
        var act = () => publisher.PublishAsync(MakeRequest(), CancellationToken.None);

        // Assert — after 3 retries, exception propagates (middleware maps to 503)
        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task PublishAsync_IncludesRequestId_InSerializedPayload()
    {
        // Arrange
        var publisher = CreatePublisher();
        var request = MakeRequest();
        NameValueEntry[]? capturedEntries = null;
        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Do<NameValueEntry[]>(e => capturedEntries = e),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisValue.EmptyString);

        // Act
        await publisher.PublishAsync(request, CancellationToken.None);

        // Assert
        var payload = capturedEntries![0].Value.ToString();
        payload.Should().Contain(request.Id.ToString());
    }
}

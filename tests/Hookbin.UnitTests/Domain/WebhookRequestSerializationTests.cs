using System.Text.Json;
using FluentAssertions;
using Hookbin.Domain.Entities;

namespace Hookbin.UnitTests.Domain;

/// <summary>
/// Regression net for the same bug class that hit <see cref="WebhookToken"/>: any property
/// with <c>private set</c> requires <c>[JsonInclude]</c> for System.Text.Json round-trip.
/// <see cref="WebhookRequest.ProcessingTimeMs"/> is the only such property on this entity.
///
/// The Redis Stream pipeline (<c>RedisStreamPublisher.PublishAsync</c>) serializes
/// <see cref="WebhookRequest"/> with bare <c>JsonSerializer.Serialize(request)</c>. If any
/// future code change calls <c>RecordProcessingTime</c> on the publisher side, the value
/// must survive the JSON round-trip to the consumer.
/// </summary>
public sealed class WebhookRequestSerializationTests
{
    private static WebhookRequest BuildRequest() => new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        TokenId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        ReceivedAt = new DateTimeOffset(2026, 5, 13, 10, 30, 45, 123, TimeSpan.Zero).AddTicks(4567),
        Method = "POST",
        Path = "/webhook/test",
        QueryString = "?foo=bar",
        Headers = "{\"X-Source\":\"unit-test\"}",
        Body = "{\"ok\":true}",
        IsBodyBase64 = false,
        ContentType = "application/json",
        IpAddress = "203.0.113.42",
        UserAgent = "curl/8.0",
        SizeBytes = 84,
        Note = "tagged for retry",
        ResponseStatusCode = 200,
        IpCountry = "JO"
    };

    [Fact]
    public void JsonRoundTrip_PreservesAllInitProperties()
    {
        var original = BuildRequest();

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookRequest>(json)!;

        revived.Id.Should().Be(original.Id);
        revived.TokenId.Should().Be(original.TokenId);
        revived.ReceivedAt.Should().Be(original.ReceivedAt);
        revived.Method.Should().Be("POST");
        revived.Path.Should().Be("/webhook/test");
        revived.QueryString.Should().Be("?foo=bar");
        revived.Headers.Should().Be("{\"X-Source\":\"unit-test\"}");
        revived.Body.Should().Be("{\"ok\":true}");
        revived.IsBodyBase64.Should().BeFalse();
        revived.ContentType.Should().Be("application/json");
        revived.IpAddress.Should().Be("203.0.113.42");
        revived.UserAgent.Should().Be("curl/8.0");
        revived.SizeBytes.Should().Be(84);
        revived.Note.Should().Be("tagged for retry");
        revived.ResponseStatusCode.Should().Be(200);
        revived.IpCountry.Should().Be("JO");
    }

    [Fact]
    public void JsonRoundTrip_PreservesProcessingTimeMs_PrivateSet()
    {
        // This is the regression target. ProcessingTimeMs is private-set; without
        // [JsonInclude] it would silently revert to null on the consumer side.
        var original = BuildRequest();
        original.RecordProcessingTime(42);

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookRequest>(json)!;

        revived.ProcessingTimeMs.Should().Be(42,
            "ProcessingTimeMs survives the Redis Stream JSON round-trip — required for accurate latency telemetry");
    }

    [Fact]
    public void JsonRoundTrip_PreservesNullProcessingTime_WhenUnset()
    {
        var original = BuildRequest();
        // RecordProcessingTime not called

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookRequest>(json)!;

        revived.ProcessingTimeMs.Should().BeNull();
    }

    [Fact]
    public void JsonRoundTrip_ReceivedAt_PreservesSubMillisecondPrecision()
    {
        // CLAUDE.md DANGER ZONE: ReceivedAt has millisecond precision pinned at the DB layer.
        // This test pins it at the JSON layer too, so the round-trip through Redis Stream doesn't lose it.
        var original = BuildRequest();

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookRequest>(json)!;

        revived.ReceivedAt.Ticks.Should().Be(original.ReceivedAt.Ticks,
            "ReceivedAt round-trips exactly — losing sub-millisecond ticks would break export consumers that rely on event ordering");
    }

    [Fact]
    public void JsonRoundTrip_NullableFieldsRemainNull()
    {
        var original = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "GET",
            Path = "/",
            Headers = "{}",
            IpAddress = "0.0.0.0"
        };

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookRequest>(json)!;

        revived.QueryString.Should().BeNull();
        revived.Body.Should().BeNull();
        revived.ContentType.Should().BeNull();
        revived.UserAgent.Should().BeNull();
        revived.Note.Should().BeNull();
        revived.ResponseStatusCode.Should().BeNull();
        revived.IpCountry.Should().BeNull();
    }
}

using System.Text.Json;
using FluentAssertions;
using Hookbin.Domain.Entities;
using Hookbin.Domain.ValueObjects;

namespace Hookbin.UnitTests.Domain;

/// <summary>
/// Regression net for the cached-token deserialization bug discovered via Chrome DevTools
/// MCP E2E walkthrough on 2026-05-13:
///
/// After commit 9062d68 (domain encapsulation refactor), `WebhookToken` properties switched
/// from public setters to `private set`. The `RedisTokenCache` round-trips entities through
/// `System.Text.Json`, whose default deserializer **does not write to private setters**.
/// Result: every cache-hit returned a token with `CustomResponse == null`, `IsActive == true`
/// (field initializer default), and empty `Name`/`Description` — silently ignoring custom
/// responses and deactivated-token gates for up to 5 minutes (the cache TTL).
///
/// Symptom seen in production walkthrough:
///   Webhook #1 (cache miss → DB) returned 500 + custom body ✅
///   Webhook #2 (cache hit  → JSON) returned 200 + default response ❌
///
/// Fix: `[JsonInclude]` on the four `private set` properties so System.Text.Json calls them
/// during deserialization. These tests would fail without that attribute.
/// </summary>
public sealed class WebhookTokenSerializationTests
{
    private static WebhookToken BuildFullyPopulatedToken()
    {
        var token = new WebhookToken
        {
            Id = Guid.Parse("111e9d5f-785e-4a58-8162-e963fe5bbac6"),
            Token = Guid.Parse("18d89b4f-19fe-4a29-b782-a4910eec8cd3"),
            CreatedAt = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero)
        };
        token.UpdateName("mcp-walkthrough");
        token.UpdateDescription("Created during E2E session");
        token.Deactivate();
        token.SetCustomResponse(new CustomResponse
        {
            StatusCode = 500,
            ContentType = "text/plain",
            Body = "MCP-CUSTOM-RESPONSE-PROOF",
            Headers = "{\"X-Custom-By\":\"chrome-devtools-mcp\"}"
        });
        return token;
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllMutableProperties()
    {
        var original = BuildFullyPopulatedToken();

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookToken>(json);

        revived.Should().NotBeNull();
        revived!.Id.Should().Be(original.Id);
        revived.Token.Should().Be(original.Token);
        revived.CreatedAt.Should().Be(original.CreatedAt);
        revived.Name.Should().Be("mcp-walkthrough",
            "private-set Name must be deserialized — the cached-token symptom otherwise lands here");
        revived.Description.Should().Be("Created during E2E session",
            "private-set Description must be deserialized");
        revived.IsActive.Should().BeFalse(
            "private-set IsActive must be deserialized; otherwise inactive tokens silently return 200 from cache");
    }

    [Fact]
    public void JsonRoundTrip_PreservesCustomResponse_SoCacheHitsServeIt()
    {
        // This is the exact scenario the MCP walkthrough caught:
        //   Set custom-response → cache populated → next webhook serves custom (500)
        //   Second webhook hits cache → JSON round-trip → CustomResponse must NOT be null
        var original = BuildFullyPopulatedToken();

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookToken>(json)!;

        revived.CustomResponse.Should().NotBeNull(
            "CustomResponse round-trip is what the receiver checks on cache-hit webhook delivery");
        revived.CustomResponse!.StatusCode.Should().Be(500);
        revived.CustomResponse.ContentType.Should().Be("text/plain");
        revived.CustomResponse.Body.Should().Be("MCP-CUSTOM-RESPONSE-PROOF");
        revived.CustomResponse.Headers.Should().Be("{\"X-Custom-By\":\"chrome-devtools-mcp\"}");
    }

    [Fact]
    public void JsonRoundTrip_PreservesInactiveState_AcrossCacheHits()
    {
        // The companion symptom: deactivated tokens served 200 from cache instead of 410.
        // The field initializer `IsActive = true` masked this in a way that's easy to miss.
        var original = new WebhookToken
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        original.Deactivate();

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookToken>(json)!;

        revived.IsActive.Should().BeFalse(
            "deactivated tokens must stay deactivated through the JSON round-trip — otherwise the receiver's 410 gate silently fails for cache-hits");
    }

    [Fact]
    public void JsonRoundTrip_DefaultsAreStable()
    {
        // Sanity check: a freshly-constructed token round-trips cleanly with its initializer defaults.
        var original = new WebhookToken
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<WebhookToken>(json)!;

        revived.Name.Should().Be(string.Empty);
        revived.Description.Should().BeNull();
        revived.IsActive.Should().BeTrue();
        revived.CustomResponse.Should().BeNull();
    }
}

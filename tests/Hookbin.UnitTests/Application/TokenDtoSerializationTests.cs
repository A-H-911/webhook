using System.Text.Json;
using FluentAssertions;
using Hookbin.Application.Tokens.Queries.GetToken;

namespace Hookbin.UnitTests.Application;

/// <summary>
/// Pins the API contract for <see cref="TokenDto"/> and <see cref="CustomResponseDto"/>:
///   - JSON property names are camelCase (ASP.NET Core default uses <c>JsonNamingPolicy.CamelCase</c>)
///   - <c>customResponse</c> is <c>null</c> (not <c>{}</c>) when no custom response is configured
///   - <c>headers</c> is a raw JSON string, NOT an object — same invariant as the domain value object
/// The Angular SPA depends on every one of these wire-shape facts.
/// </summary>
public sealed class TokenDtoSerializationTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serialize_UsesCamelCasePropertyNames_PerWebDefaults()
    {
        var dto = new TokenDto(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Token: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name: "my-hook",
            WebhookUrl: "https://example.com/webhook/22222222-2222-2222-2222-222222222222",
            Description: "desc",
            CreatedAt: new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero),
            IsActive: true,
            CustomResponse: null);

        var json = JsonSerializer.Serialize(dto, WebOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("id", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("token", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("name", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("webhookUrl", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("description", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("createdAt", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("isActive", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("customResponse", out _).Should().BeTrue();
    }

    [Fact]
    public void NullCustomResponse_SerializesAsJsonNull_NotEmptyObject()
    {
        // Angular's UI checks `token.customResponse == null` to decide whether to show
        // the "200 custom" badge. If we ever serialize null as `{}`, the badge appears
        // on every endpoint by mistake.
        var dto = new TokenDto(
            Id: Guid.NewGuid(),
            Token: Guid.NewGuid(),
            Name: "no-custom",
            WebhookUrl: "https://example.com",
            Description: null,
            CreatedAt: DateTimeOffset.UtcNow,
            IsActive: true,
            CustomResponse: null);

        var json = JsonSerializer.Serialize(dto, WebOptions);
        using var doc = JsonDocument.Parse(json);

        var cr = doc.RootElement.GetProperty("customResponse");
        cr.ValueKind.Should().Be(JsonValueKind.Null,
            "customResponse is null in the wire payload — not an empty object");
    }

    [Fact]
    public void CustomResponseDto_HeadersField_IsStringWireShape()
    {
        var cr = new CustomResponseDto(
            StatusCode: 201,
            ContentType: "application/json",
            Body: "{\"ok\":true}",
            Headers: "{\"X-Custom\":\"v\"}");

        var json = JsonSerializer.Serialize(cr, WebOptions);
        using var doc = JsonDocument.Parse(json);

        var headers = doc.RootElement.GetProperty("headers");
        headers.ValueKind.Should().Be(JsonValueKind.String,
            "headers is a JSON string literal — the Angular dialog validates it with JSON.parse and sends it back as-is");
        headers.GetString().Should().Be("{\"X-Custom\":\"v\"}");
    }

    [Fact]
    public void JsonRoundTrip_TokenDto_PreservesAllFields()
    {
        var original = new TokenDto(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Token: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name: "with-everything",
            WebhookUrl: "https://hookbin.example/webhook/44444444-4444-4444-4444-444444444444",
            Description: "all fields populated",
            CreatedAt: new DateTimeOffset(2026, 5, 13, 12, 34, 56, TimeSpan.Zero),
            IsActive: false,
            CustomResponse: new CustomResponseDto(
                StatusCode: 503,
                ContentType: "text/plain",
                Body: "down",
                Headers: "{\"Retry-After\":\"60\"}"));

        var json = JsonSerializer.Serialize(original, WebOptions);
        var revived = JsonSerializer.Deserialize<TokenDto>(json, WebOptions)!;

        revived.Should().Be(original, "record equality + camelCase round-trip pin the full wire contract");
        revived.CustomResponse.Should().NotBeNull();
        revived.CustomResponse!.Body.Should().Be("down");
        revived.IsActive.Should().BeFalse();
    }

    [Fact]
    public void JsonRoundTrip_NullableDescription_StaysNull()
    {
        var original = new TokenDto(
            Id: Guid.NewGuid(),
            Token: Guid.NewGuid(),
            Name: "no-desc",
            WebhookUrl: "https://example.com",
            Description: null,
            CreatedAt: DateTimeOffset.UtcNow,
            IsActive: true,
            CustomResponse: null);

        var json = JsonSerializer.Serialize(original, WebOptions);
        var revived = JsonSerializer.Deserialize<TokenDto>(json, WebOptions)!;

        revived.Description.Should().BeNull();
    }
}

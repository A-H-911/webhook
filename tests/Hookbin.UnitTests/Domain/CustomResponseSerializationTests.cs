using System.Text.Json;
using FluentAssertions;
using Hookbin.Domain.ValueObjects;

namespace Hookbin.UnitTests.Domain;

/// <summary>
/// Pins the contract for <see cref="CustomResponse"/> across the Redis cache JSON round-trip.
///
/// Critical invariant from CLAUDE.md DANGER ZONE:
///   "Headers field type 'string' on both C# and Angular — a raw JSON string like
///   '{\"X-Foo\":\"bar\"}', NOT a Record&lt;string,string&gt;."
///
/// Changing <c>Headers</c> from <c>string</c> to <c>Dictionary&lt;string, string&gt;</c> on
/// either side would silently break the receiver's header forwarding (the receiver does
/// <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string, string&gt;&gt;(custom.Headers)</c>
/// and swallows <c>JsonException</c>). These tests pin the wire shape.
/// </summary>
public sealed class CustomResponseSerializationTests
{
    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new CustomResponse
        {
            StatusCode = 418,
            ContentType = "text/plain",
            Body = "I am a teapot",
            Headers = "{\"X-Custom\":\"pinned\",\"X-Another\":\"value\"}"
        };

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<CustomResponse>(json)!;

        revived.StatusCode.Should().Be(418);
        revived.ContentType.Should().Be("text/plain");
        revived.Body.Should().Be("I am a teapot");
        revived.Headers.Should().Be("{\"X-Custom\":\"pinned\",\"X-Another\":\"value\"}");
    }

    [Fact]
    public void JsonRoundTrip_NullBody_StaysNull()
    {
        var original = new CustomResponse
        {
            StatusCode = 204,
            ContentType = "text/plain",
            Body = null,
            Headers = "{}"
        };

        var json = JsonSerializer.Serialize(original);
        var revived = JsonSerializer.Deserialize<CustomResponse>(json)!;

        revived.Body.Should().BeNull("204 No Content responses with no body must round-trip as null, not empty string");
    }

    [Fact]
    public void Headers_AreSerializedAsString_NotObject()
    {
        // The DANGER ZONE invariant: Headers is a raw JSON string. If someone changes the
        // C# type to Dictionary<string,string>, the serialized JSON would contain
        // `"Headers":{"X-Foo":"bar"}` (an object) rather than `"Headers":"{...}"` (a string).
        var cr = new CustomResponse
        {
            StatusCode = 200,
            ContentType = "application/json",
            Headers = "{\"X-Foo\":\"bar\"}"
        };

        var json = JsonSerializer.Serialize(cr);
        using var doc = JsonDocument.Parse(json);
        var headersProp = doc.RootElement.GetProperty("Headers");

        headersProp.ValueKind.Should().Be(JsonValueKind.String,
            "Headers must serialize as a JSON string literal, NOT an embedded object");
        headersProp.GetString().Should().Be("{\"X-Foo\":\"bar\"}");
    }

    [Fact]
    public void HeadersDefault_IsEmptyJsonObjectLiteral()
    {
        // The default value `"{}"` on the property is the contract.
        // Changing it to "" or "null" would break the receiver's JSON.Parse on the Angular side.
        var cr = new CustomResponse { StatusCode = 200, ContentType = "text/plain" };

        cr.Headers.Should().Be("{}");
    }

    [Fact]
    public void RecordEquality_BySemanticValue()
    {
        // CustomResponse is a `record` — equality is structural. Pinning this prevents
        // someone changing it to `class` and silently breaking value-based comparisons.
        var a = new CustomResponse
        {
            StatusCode = 200,
            ContentType = "text/plain",
            Body = "x",
            Headers = "{}"
        };
        var b = new CustomResponse
        {
            StatusCode = 200,
            ContentType = "text/plain",
            Body = "x",
            Headers = "{}"
        };

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}

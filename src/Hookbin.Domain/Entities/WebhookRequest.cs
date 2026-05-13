using System.Text.Json.Serialization;

namespace Hookbin.Domain.Entities;

public sealed class WebhookRequest
{
    public Guid Id { get; init; }
    public Guid TokenId { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? QueryString { get; init; }
    public string Headers { get; init; } = "{}";
    public string? Body { get; init; }
    public bool IsBodyBase64 { get; init; }
    public string? ContentType { get; init; }
    public string IpAddress { get; init; } = "unknown";
    public string? UserAgent { get; init; }
    public long SizeBytes { get; init; }
    // [JsonInclude] is required so the Redis Stream JSON round-trip preserves the value
    // set via RecordProcessingTime(). System.Text.Json's default deserializer skips
    // private-set properties; without this attribute, the publisher would silently lose
    // ProcessingTimeMs on its way through the queue. See WebhookRequestSerializationTests.
    [JsonInclude] public long? ProcessingTimeMs { get; private set; }
    public string? Note { get; init; }
    public int? ResponseStatusCode { get; init; }
    public string? IpCountry { get; init; }

    public WebhookToken? Token { get; init; }

    public void RecordProcessingTime(long milliseconds) =>
        ProcessingTimeMs = Math.Max(0L, milliseconds);
}

using System.Text.Json.Serialization;
using Hookbin.Domain.ValueObjects;

namespace Hookbin.Domain.Entities;

public sealed class WebhookToken
{
    public Guid Id { get; init; }
    public Guid Token { get; init; }
    // [JsonInclude] is required because System.Text.Json's default behavior skips
    // properties with non-public setters. Without it, RedisTokenCache cache-hits
    // would silently return tokens with default values for these fields — losing
    // CustomResponse and the deactivated flag for up to 5 minutes (the cache TTL).
    // See WebhookTokenSerializationTests for the regression net.
    [JsonInclude] public string Name { get; private set; } = string.Empty;
    [JsonInclude] public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    [JsonInclude] public bool IsActive { get; private set; } = true;
    [JsonInclude] public CustomResponse? CustomResponse { get; private set; }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Token name is required.", nameof(name));
        if (name.Length > 80)
            throw new ArgumentException("Token name must not exceed 80 characters.", nameof(name));
        Name = name.Trim();
    }

    public void UpdateDescription(string? description) => Description = description;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void SetCustomResponse(CustomResponse response) => CustomResponse = response;
    public void ClearCustomResponse() => CustomResponse = null;
}

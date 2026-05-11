using WebhookService.Domain.ValueObjects;

namespace WebhookService.Domain.Entities;

public sealed class WebhookToken
{
    public Guid Id { get; init; }
    public Guid Token { get; init; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsActive { get; private set; } = true;
    public CustomResponse? CustomResponse { get; private set; }

    public void UpdateDescription(string? description) => Description = description;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void SetCustomResponse(CustomResponse response) => CustomResponse = response;
    public void ClearCustomResponse() => CustomResponse = null;
}

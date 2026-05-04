using WebhookService.Domain.ValueObjects;

namespace WebhookService.Domain.Entities;

public sealed class WebhookToken
{
    public Guid Id { get; init; }
    public Guid Token { get; init; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsActive { get; set; } = true;
    public CustomResponse? CustomResponse { get; set; }
}

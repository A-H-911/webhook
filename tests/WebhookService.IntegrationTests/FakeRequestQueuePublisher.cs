using Microsoft.Extensions.DependencyInjection;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.IntegrationTests;

internal sealed class FakeRequestQueuePublisher(IServiceScopeFactory scopeFactory) : IRequestQueuePublisher
{
    public async Task PublishAsync(WebhookRequest request, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookRequestRepository>();
        await repo.AddAsync(request, ct);
    }
}

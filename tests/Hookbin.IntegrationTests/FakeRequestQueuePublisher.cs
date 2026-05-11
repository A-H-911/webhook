using Microsoft.Extensions.DependencyInjection;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;
using Hookbin.Domain.Services;

namespace Hookbin.IntegrationTests;

internal sealed class FakeRequestQueuePublisher(IServiceScopeFactory scopeFactory) : IRequestQueuePublisher
{
    public async Task PublishAsync(WebhookRequest request, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookRequestRepository>();
        await repo.AddAsync(request, ct);
    }
}

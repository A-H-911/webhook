using MediatR;
using Microsoft.Extensions.Caching.Memory;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.Application.Tokens.Commands.DeleteToken;

internal sealed class DeleteTokenCommandHandler(
    IWebhookTokenRepository repository,
    ISseNotifier sseNotifier,
    IMemoryCache cache)
    : IRequestHandler<DeleteTokenCommand, bool>
{
    public async Task<bool> Handle(DeleteTokenCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null)
            return false;

        token.IsActive = false;
        await repository.UpdateAsync(token, cancellationToken);
        cache.Remove($"token:{token.Token}");
        sseNotifier.NotifyTokenDeleted(token.Id);
        return true;
    }
}

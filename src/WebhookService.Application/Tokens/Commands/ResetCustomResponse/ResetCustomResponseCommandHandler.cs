using MediatR;
using Microsoft.Extensions.Caching.Memory;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Commands.ResetCustomResponse;

internal sealed class ResetCustomResponseCommandHandler(
    IWebhookTokenRepository repository,
    IMemoryCache cache)
    : IRequestHandler<ResetCustomResponseCommand, bool>
{
    public async Task<bool> Handle(ResetCustomResponseCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null) return false;

        token.CustomResponse = null;
        await repository.UpdateAsync(token, cancellationToken);

        cache.Remove($"token:{token.Token}");
        return true;
    }
}

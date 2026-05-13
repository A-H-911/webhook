using MediatR;
using Hookbin.Application.Caching;
using Hookbin.Domain.Repositories;
using Hookbin.Domain.Services;

namespace Hookbin.Application.Tokens.Commands.DeleteToken;

internal sealed class DeleteTokenCommandHandler(
    IWebhookTokenRepository repository,
    ISseNotifier sseNotifier,
    ITokenCache tokenCache)
    : IRequestHandler<DeleteTokenCommand, bool>
{
    public async Task<bool> Handle(DeleteTokenCommand request, CancellationToken cancellationToken)
    {
        // GetByIdIncludingInactiveAsync so previously soft-deleted tokens can also be cleaned up
        var token = await repository.GetByIdIncludingInactiveAsync(request.Id, cancellationToken);
        if (token is null)
            return false;

        await repository.DeleteAsync(token.Id, cancellationToken);
        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        sseNotifier.NotifyTokenDeleted(token.Id);
        return true;
    }
}

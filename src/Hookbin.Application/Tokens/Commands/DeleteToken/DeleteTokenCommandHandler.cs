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
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null)
            return false;

        token.Deactivate();
        await repository.UpdateAsync(token, cancellationToken);
        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        sseNotifier.NotifyTokenDeleted(token.Id);
        return true;
    }
}

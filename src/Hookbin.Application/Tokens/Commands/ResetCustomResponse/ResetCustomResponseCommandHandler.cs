using MediatR;
using Hookbin.Application.Caching;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Tokens.Commands.ResetCustomResponse;

internal sealed class ResetCustomResponseCommandHandler(
    IWebhookTokenRepository repository,
    ITokenCache tokenCache)
    : IRequestHandler<ResetCustomResponseCommand, bool>
{
    public async Task<bool> Handle(ResetCustomResponseCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null) return false;

        token.ClearCustomResponse();
        await repository.UpdateAsync(token, cancellationToken);

        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        return true;
    }
}

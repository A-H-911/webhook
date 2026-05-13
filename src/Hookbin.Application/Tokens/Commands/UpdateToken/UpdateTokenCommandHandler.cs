using MediatR;
using Microsoft.Extensions.Options;
using Hookbin.Application.Caching;
using Hookbin.Application.Options;
using Hookbin.Application.Tokens.Queries.GetToken;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Tokens.Commands.UpdateToken;

internal sealed class UpdateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options,
    ITokenCache tokenCache)
    : IRequestHandler<UpdateTokenCommand, TokenDto?>
{
    public async Task<TokenDto?> Handle(UpdateTokenCommand request, CancellationToken cancellationToken)
    {
        // GetByIdIncludingInactiveAsync (not GetByIdAsync) so admins can reactivate a
        // previously-deactivated token. GetByIdAsync filters IsActive=false out, which
        // would silently turn the PUT into a 404 for any deactivated token.
        var token = await repository.GetByIdIncludingInactiveAsync(request.Id, cancellationToken);
        if (token is null)
            return null;

        token.UpdateName(request.Name);
        token.UpdateDescription(request.Description);
        if (request.IsActive) token.Activate(); else token.Deactivate();

        await repository.UpdateAsync(token, cancellationToken);
        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        return token.ToDto(options.Value.BaseUrl);
    }
}

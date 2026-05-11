using MediatR;
using Microsoft.Extensions.Options;
using WebhookService.Application.Caching;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Commands.UpdateToken;

internal sealed class UpdateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options,
    ITokenCache tokenCache)
    : IRequestHandler<UpdateTokenCommand, TokenDto?>
{
    public async Task<TokenDto?> Handle(UpdateTokenCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null)
            return null;

        token.UpdateDescription(request.Description);
        if (request.IsActive) token.Activate(); else token.Deactivate();

        await repository.UpdateAsync(token, cancellationToken);
        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        return token.ToDto(options.Value.BaseUrl);
    }
}

using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Commands.UpdateToken;

internal sealed class UpdateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options,
    IMemoryCache cache)
    : IRequestHandler<UpdateTokenCommand, TokenDto?>
{
    public async Task<TokenDto?> Handle(UpdateTokenCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null)
            return null;

        token.Description = request.Description;
        token.IsActive = request.IsActive;

        await repository.UpdateAsync(token, cancellationToken);
        cache.Remove($"token:{token.Token}");
        return token.ToDto(options.Value.BaseUrl);
    }
}

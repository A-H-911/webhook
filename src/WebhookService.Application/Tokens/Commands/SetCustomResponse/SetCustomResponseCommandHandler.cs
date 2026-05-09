using MediatR;
using WebhookService.Application.Caching;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.Application.Tokens.Commands.SetCustomResponse;

internal sealed class SetCustomResponseCommandHandler(
    IWebhookTokenRepository repository,
    ITokenCache tokenCache)
    : IRequestHandler<SetCustomResponseCommand, bool>
{
    public async Task<bool> Handle(SetCustomResponseCommand request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.TokenId, cancellationToken);
        if (token is null)
            return false;

        token.CustomResponse = new CustomResponse
        {
            StatusCode = request.StatusCode,
            ContentType = request.ContentType,
            Body = request.Body,
            Headers = request.Headers
        };

        await repository.UpdateAsync(token, cancellationToken);
        await tokenCache.RemoveAsync(token.Token, cancellationToken);
        return true;
    }
}

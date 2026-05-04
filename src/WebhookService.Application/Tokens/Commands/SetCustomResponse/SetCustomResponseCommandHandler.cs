using MediatR;
using Microsoft.Extensions.Caching.Memory;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.Application.Tokens.Commands.SetCustomResponse;

internal sealed class SetCustomResponseCommandHandler(
    IWebhookTokenRepository repository,
    IMemoryCache cache)
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
        cache.Remove($"token:{token.Token}");
        return true;
    }
}

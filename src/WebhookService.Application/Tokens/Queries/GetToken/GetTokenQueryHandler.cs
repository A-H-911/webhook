using MediatR;
using Microsoft.Extensions.Options;
using WebhookService.Application.Options;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Queries.GetToken;

internal sealed class GetTokenQueryHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options)
    : IRequestHandler<GetTokenQuery, TokenDto?>
{
    public async Task<TokenDto?> Handle(GetTokenQuery request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null) return null;
        return token.ToDto(options.Value.BaseUrl);
    }
}

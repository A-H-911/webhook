using MediatR;
using Microsoft.Extensions.Options;
using Hookbin.Application.Options;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Tokens.Queries.GetToken;

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

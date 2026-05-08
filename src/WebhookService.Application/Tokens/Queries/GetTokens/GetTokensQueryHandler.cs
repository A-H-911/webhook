using MediatR;
using Microsoft.Extensions.Options;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Queries.GetTokens;

internal sealed class GetTokensQueryHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options)
    : IRequestHandler<GetTokensQuery, IReadOnlyList<TokenDto>>
{
    public async Task<IReadOnlyList<TokenDto>> Handle(GetTokensQuery request, CancellationToken cancellationToken)
    {
        var tokens = await repository.GetAllActiveAsync(cancellationToken);
        return tokens.Select(t => t.ToDto(options.Value.BaseUrl)).ToList();
    }
}

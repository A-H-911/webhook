using MediatR;
using Microsoft.Extensions.Configuration;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Queries.GetTokens;

internal sealed class GetTokensQueryHandler(
    IWebhookTokenRepository repository,
    IConfiguration configuration)
    : IRequestHandler<GetTokensQuery, IReadOnlyList<TokenDto>>
{
    public async Task<IReadOnlyList<TokenDto>> Handle(GetTokensQuery request, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Webhook:BaseUrl"] ?? string.Empty;
        var tokens = await repository.GetAllActiveAsync(cancellationToken);
        return tokens.Select(t => t.ToDto(baseUrl)).ToList();
    }
}

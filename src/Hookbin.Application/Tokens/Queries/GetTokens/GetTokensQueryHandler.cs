using MediatR;
using Microsoft.Extensions.Options;
using Hookbin.Application.Options;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Tokens.Queries.GetTokens;

internal sealed class GetTokensQueryHandler(
    IWebhookTokenRepository tokenRepository,
    IWebhookRequestRepository requestRepository,
    IOptions<WebhookOptions> options)
    : IRequestHandler<GetTokensQuery, TokensPagedResult>
{
    public async Task<TokensPagedResult> Handle(GetTokensQuery request, CancellationToken cancellationToken)
    {
        var (rows, total) = await tokenRepository.GetPagedWithStatsAsync(
            request.Skip, request.Take, cancellationToken);

        var tokenIds = rows.Select(r => r.Token.Id);
        var sparklines = await requestRepository.GetSparklineBatchAsync(tokenIds, cancellationToken);

        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var items = rows.Select(r => new TokenListItemDto(
            r.Token.Id,
            r.Token.Token,
            r.Token.Name,
            $"{baseUrl}/webhook/{r.Token.Token}",
            r.Token.Description,
            r.Token.CreatedAt,
            r.Token.IsActive,
            r.Token.CustomResponse is not null,
            r.LifetimeRequestCount,
            r.RequestCount24h,
            sparklines.GetValueOrDefault(r.Token.Id, new int[24]),
            r.LastReceivedAt))
            .ToList();

        return new TokensPagedResult(items, total, request.Skip + request.Take < total);
    }
}

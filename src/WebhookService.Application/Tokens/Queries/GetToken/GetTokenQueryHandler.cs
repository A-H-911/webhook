using MediatR;
using Microsoft.Extensions.Configuration;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Queries.GetToken;

internal sealed class GetTokenQueryHandler(
    IWebhookTokenRepository repository,
    IConfiguration configuration)
    : IRequestHandler<GetTokenQuery, TokenDto?>
{
    public async Task<TokenDto?> Handle(GetTokenQuery request, CancellationToken cancellationToken)
    {
        var token = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (token is null) return null;
        var baseUrl = configuration["Webhook:BaseUrl"] ?? string.Empty;
        return token.ToDto(baseUrl);
    }
}

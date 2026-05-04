using MediatR;
using Microsoft.Extensions.Configuration;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Commands.UpdateToken;

internal sealed class UpdateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IConfiguration configuration)
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
        var baseUrl = configuration["Webhook:BaseUrl"] ?? string.Empty;
        return token.ToDto(baseUrl);
    }
}

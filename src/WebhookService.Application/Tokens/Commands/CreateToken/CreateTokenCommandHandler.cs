using MediatR;
using Microsoft.Extensions.Configuration;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.Application.Tokens.Commands.CreateToken;

internal sealed class CreateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IConfiguration configuration)
    : IRequestHandler<CreateTokenCommand, TokenDto>
{
    public async Task<TokenDto> Handle(CreateTokenCommand request, CancellationToken cancellationToken)
    {
        var token = new WebhookToken
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid(),
            Description = request.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(token, cancellationToken);
        var baseUrl = configuration["Webhook:BaseUrl"] ?? string.Empty;
        return token.ToDto(baseUrl);
    }
}

using MediatR;
using Microsoft.Extensions.Options;
using Hookbin.Application.Options;
using Hookbin.Application.Tokens.Queries.GetToken;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Tokens.Commands.CreateToken;

internal sealed class CreateTokenCommandHandler(
    IWebhookTokenRepository repository,
    IOptions<WebhookOptions> options)
    : IRequestHandler<CreateTokenCommand, TokenDto>
{
    public async Task<TokenDto> Handle(CreateTokenCommand request, CancellationToken cancellationToken)
    {
        var token = new WebhookToken
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        token.UpdateName(request.Name);
        token.UpdateDescription(request.Description);

        await repository.AddAsync(token, cancellationToken);
        return token.ToDto(options.Value.BaseUrl);
    }
}

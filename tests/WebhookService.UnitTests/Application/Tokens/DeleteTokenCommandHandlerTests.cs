using FluentAssertions;
using NSubstitute;
using WebhookService.Application.Caching;
using WebhookService.Application.Tokens.Commands.DeleteToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.Services;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class DeleteTokenCommandHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly ISseNotifier _sse = Substitute.For<ISseNotifier>();
    private readonly ITokenCache _tokenCache = Substitute.For<ITokenCache>();

    private DeleteTokenCommandHandler CreateHandler() => new(_repo, _sse, _tokenCache);

    [Fact]
    public async Task Handle_ReturnsFalse_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);

        var result = await CreateHandler().Handle(new DeleteTokenCommand(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeFalse();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsTrue_AndDeactivatesToken()
    {
        var id = Guid.NewGuid();
        var token = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var result = await CreateHandler().Handle(new DeleteTokenCommand(id), CancellationToken.None);

        result.Should().BeTrue();
        token.IsActive.Should().BeFalse();
        await _repo.Received(1).UpdateAsync(token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotifiesSseAndClearsCache_OnDelete()
    {
        var id = Guid.NewGuid();
        var token = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        await CreateHandler().Handle(new DeleteTokenCommand(id), CancellationToken.None);

        _sse.Received(1).NotifyTokenDeleted(id);
        await _tokenCache.Received(1).RemoveAsync(token.Token, Arg.Any<CancellationToken>());
    }
}

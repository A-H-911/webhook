using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookService.Application.Caching;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Commands.UpdateToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class UpdateTokenCommandHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly ITokenCache _tokenCache = Substitute.For<ITokenCache>();
    private readonly IOptions<WebhookOptions> _options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
    {
        BaseUrl = "https://example.com",
        RetentionDays = 7,
        MaxRequestSizeMb = 5
    });

    private UpdateTokenCommandHandler CreateHandler() => new(_repo, _options, _tokenCache);

    private static WebhookToken MakeToken(Guid id) => new()
    {
        Id = id,
        Token = Guid.NewGuid(),
        Description = "original",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true
    };

    [Fact]
    public async Task Handle_ReturnsUpdatedDto_WhenTokenExists()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var result = await CreateHandler().Handle(new UpdateTokenCommand(id, "updated", false), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Description.Should().Be("updated");
        result.IsActive.Should().BeFalse();
        await _repo.Received(1).UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);

        var result = await CreateHandler().Handle(
            new UpdateTokenCommand(Guid.NewGuid(), "x", true), CancellationToken.None);

        result.Should().BeNull();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovesCacheEntry_AfterUpdate()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        await CreateHandler().Handle(new UpdateTokenCommand(id, "new", true), CancellationToken.None);

        await _tokenCache.Received(1).RemoveAsync(token.Token, Arg.Any<CancellationToken>());
    }
}

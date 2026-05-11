using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Hookbin.Application.Caching;
using Hookbin.Application.Options;
using Hookbin.Application.Tokens.Commands.UpdateToken;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Tokens;

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

    private static WebhookToken MakeToken(Guid id)
    {
        var t = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        t.UpdateName("original-name");
        t.UpdateDescription("original");
        return t;
    }

    [Fact]
    public async Task Handle_ReturnsUpdatedDto_WhenTokenExists()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var result = await CreateHandler().Handle(new UpdateTokenCommand(id, "new-name", "updated", false), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("new-name");
        result.Description.Should().Be("updated");
        result.IsActive.Should().BeFalse();
        await _repo.Received(1).UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);

        var result = await CreateHandler().Handle(
            new UpdateTokenCommand(Guid.NewGuid(), "test-name", "x", true), CancellationToken.None);

        result.Should().BeNull();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovesCacheEntry_AfterUpdate()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        await CreateHandler().Handle(new UpdateTokenCommand(id, "new-name", "new", true), CancellationToken.None);

        await _tokenCache.Received(1).RemoveAsync(token.Token, Arg.Any<CancellationToken>());
    }
}

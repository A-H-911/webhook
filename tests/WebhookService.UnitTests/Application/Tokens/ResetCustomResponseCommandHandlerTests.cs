using FluentAssertions;
using NSubstitute;
using WebhookService.Application.Caching;
using WebhookService.Application.Tokens.Commands.ResetCustomResponse;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class ResetCustomResponseCommandHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly ITokenCache _tokenCache = Substitute.For<ITokenCache>();

    private ResetCustomResponseCommandHandler CreateHandler() => new(_repo, _tokenCache);

    [Fact]
    public async Task Handle_ReturnsFalse_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);

        var result = await CreateHandler().Handle(new ResetCustomResponseCommand(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ClearsCustomResponse_AndReturnsTrue()
    {
        var id = Guid.NewGuid();
        var token = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.SetCustomResponse(new CustomResponse { StatusCode = 201, ContentType = "application/json" });
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var result = await CreateHandler().Handle(new ResetCustomResponseCommand(id), CancellationToken.None);

        result.Should().BeTrue();
        token.CustomResponse.Should().BeNull();
        await _repo.Received(1).UpdateAsync(token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovesCacheEntry_AfterReset()
    {
        var id = Guid.NewGuid();
        var token = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        await CreateHandler().Handle(new ResetCustomResponseCommand(id), CancellationToken.None);

        await _tokenCache.Received(1).RemoveAsync(token.Token, Arg.Any<CancellationToken>());
    }
}

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using WebhookService.Application.Tokens.Commands.SetCustomResponse;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class SetCustomResponseCommandHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private SetCustomResponseCommandHandler CreateHandler() => new(_repo, _cache);

    private static WebhookToken MakeToken(Guid id) => new()
    {
        Id = id,
        Token = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true
    };

    [Fact]
    public async Task Handle_ReturnsFalse_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);
        var cmd = new SetCustomResponseCommand(Guid.NewGuid(), 200, "text/plain", null, "{}");

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.Should().BeFalse();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SetsCustomResponse_AndReturnsTrue()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);
        var cmd = new SetCustomResponseCommand(id, 201, "application/json", "{\"ok\":true}", "{}");

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.Should().BeTrue();
        token.CustomResponse.Should().NotBeNull();
        token.CustomResponse!.StatusCode.Should().Be(201);
        token.CustomResponse.ContentType.Should().Be("application/json");
        token.CustomResponse.Body.Should().Be("{\"ok\":true}");
        await _repo.Received(1).UpdateAsync(token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovesCacheEntry_AfterSet()
    {
        var id = Guid.NewGuid();
        var token = MakeToken(id);
        _cache.Set($"token:{token.Token}", token);
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        await CreateHandler().Handle(new SetCustomResponseCommand(id, 200, "text/plain", null, "{}"), CancellationToken.None);

        _cache.TryGetValue($"token:{token.Token}", out _).Should().BeFalse();
    }
}
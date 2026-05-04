using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class GetTokenQueryHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Webhook:BaseUrl"] = "https://example.com" })
        .Build();

    private GetTokenQueryHandler CreateHandler() => new(_repo, _config);

    private static WebhookToken MakeToken(Guid id) => new()
    {
        Id = id,
        Token = Guid.NewGuid(),
        Description = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true
    };

    [Fact]
    public async Task Handle_ReturnsDto_WhenTokenExists()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(MakeToken(id));

        var result = await CreateHandler().Handle(new GetTokenQuery(id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.WebhookUrl.Should().Contain("https://example.com/webhook/");
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenTokenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookToken?)null);

        var result = await CreateHandler().Handle(new GetTokenQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FallsBackToEmpty_WhenBaseUrlMissing()
    {
        var config = new ConfigurationBuilder().Build();
        var handler = new GetTokenQueryHandler(_repo, config);
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(MakeToken(id));

        var result = await handler.Handle(new GetTokenQuery(id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.WebhookUrl.Should().StartWith("/webhook/");
    }
}

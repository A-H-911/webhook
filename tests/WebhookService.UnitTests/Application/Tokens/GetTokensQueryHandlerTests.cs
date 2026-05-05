using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using WebhookService.Application.Tokens.Queries.GetTokens;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class GetTokensQueryHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Webhook:BaseUrl"] = "https://example.com" })
        .Build();

    private GetTokensQueryHandler CreateHandler() => new(_repo, _config);

    [Fact]
    public async Task Handle_ReturnsMappedDtos_ForAllActiveTokens()
    {
        var tokens = new List<WebhookToken>
        {
            new() { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, IsActive = true },
            new() { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, IsActive = true }
        };
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(tokens);

        var result = await CreateHandler().Handle(new GetTokensQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.All(t => t.WebhookUrl.Contains("https://example.com/webhook/")).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoTokensExist()
    {
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WebhookToken>());

        var result = await CreateHandler().Handle(new GetTokensQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FallsBackToEmpty_WhenBaseUrlMissing()
    {
        var config = new ConfigurationBuilder().Build();
        var handler = new GetTokensQueryHandler(_repo, config);
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WebhookToken> { token });

        var result = await handler.Handle(new GetTokensQuery(), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].WebhookUrl.Should().StartWith("/webhook/");
    }
}

using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Queries.GetTokens;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class GetTokensQueryHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IOptions<WebhookOptions> _options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
    {
        BaseUrl = "https://example.com",
        MaxRequestSizeMb = 5,
        RetentionDays = 7
    });

    private GetTokensQueryHandler CreateHandler() => new(_repo, _options);

    [Fact]
    public async Task Handle_ReturnsMappedDtos_ForAllActiveTokens()
    {
        var tokens = new List<WebhookToken>
        {
            new() { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow }
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
    public async Task Handle_ProducesRelativeUrls_WhenBaseUrlIsEmpty()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions { BaseUrl = "", MaxRequestSizeMb = 5, RetentionDays = 7 });
        var handler = new GetTokensQueryHandler(_repo, options);
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WebhookToken> { token });

        var result = await handler.Handle(new GetTokensQuery(), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].WebhookUrl.Should().StartWith("/webhook/");
    }
}

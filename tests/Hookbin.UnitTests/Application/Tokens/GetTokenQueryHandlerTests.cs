using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Hookbin.Application.Options;
using Hookbin.Application.Tokens.Queries.GetToken;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Tokens;

public sealed class GetTokenQueryHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IOptions<WebhookOptions> _options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
    {
        BaseUrl = "https://example.com",
        MaxRequestSizeMb = 5,
        RetentionDays = 7
    });

    private GetTokenQueryHandler CreateHandler() => new(_repo, _options);

    private static WebhookToken MakeToken(Guid id)
    {
        var t = new WebhookToken { Id = id, Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        t.UpdateDescription("test");
        return t;
    }

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
    public async Task Handle_ProducesRelativeUrl_WhenBaseUrlIsEmpty()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions { BaseUrl = "", MaxRequestSizeMb = 5, RetentionDays = 7 });
        var handler = new GetTokenQueryHandler(_repo, options);
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(MakeToken(id));

        var result = await handler.Handle(new GetTokenQuery(id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.WebhookUrl.Should().StartWith("/webhook/");
    }

    [Fact]
    public async Task Handle_TrimsTrailingSlashFromBaseUrl()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
        {
            BaseUrl = "https://example.com/",
            MaxRequestSizeMb = 5,
            RetentionDays = 7
        });
        var handler = new GetTokenQueryHandler(_repo, options);
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(MakeToken(id));

        var result = await handler.Handle(new GetTokenQuery(id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.WebhookUrl.Should().NotContain("//webhook/");
        result.WebhookUrl.Should().Contain("https://example.com/webhook/");
    }
}

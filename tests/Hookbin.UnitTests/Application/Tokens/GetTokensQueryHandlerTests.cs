using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Hookbin.Application.Options;
using Hookbin.Application.Tokens.Queries.GetTokens;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Tokens;

public sealed class GetTokensQueryHandlerTests
{
    private readonly IWebhookTokenRepository _tokenRepo = Substitute.For<IWebhookTokenRepository>();
    private readonly IWebhookRequestRepository _requestRepo = Substitute.For<IWebhookRequestRepository>();
    private readonly IOptions<WebhookOptions> _options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
    {
        BaseUrl = "https://example.com",
        MaxRequestSizeMb = 5,
        RetentionDays = 7
    });

    private GetTokensQueryHandler CreateHandler() => new(_tokenRepo, _requestRepo, _options);

    private static WebhookToken MakeToken()
    {
        var t = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        t.UpdateName("test-hook");
        return t;
    }

    private static TokenPageRow MakeRow(WebhookToken token) =>
        new(token, LifetimeRequestCount: 5, RequestCount24h: 1, LastReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_ReturnsMappedDtos_WithCorrectWebhookUrls()
    {
        var t1 = MakeToken();
        var t2 = MakeToken();
        var rows = new List<TokenPageRow> { MakeRow(t1), MakeRow(t2) };
        _tokenRepo.GetPagedWithStatsAsync(0, 50, Arg.Any<CancellationToken>())
            .Returns((rows, 2));
        _requestRepo.GetSparklineBatchAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int[]>());

        var result = await CreateHandler().Handle(new GetTokensQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.HasMore.Should().BeFalse();
        result.Items.All(t => t.WebhookUrl.Contains("https://example.com/webhook/")).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyPage_WhenNoTokensExist()
    {
        _tokenRepo.GetPagedWithStatsAsync(0, 50, Arg.Any<CancellationToken>())
            .Returns((new List<TokenPageRow>(), 0));
        _requestRepo.GetSparklineBatchAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int[]>());

        var result = await CreateHandler().Handle(new GetTokensQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SetsHasMore_WhenMorePagesExist()
    {
        var rows = Enumerable.Range(0, 50).Select(_ => MakeRow(MakeToken())).ToList();
        _tokenRepo.GetPagedWithStatsAsync(0, 50, Arg.Any<CancellationToken>())
            .Returns((rows, 120));
        _requestRepo.GetSparklineBatchAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int[]>());

        var result = await CreateHandler().Handle(new GetTokensQuery(Skip: 0, Take: 50), CancellationToken.None);

        result.HasMore.Should().BeTrue();
        result.Total.Should().Be(120);
    }

    [Fact]
    public async Task Handle_MapsSparkline_FromBatchResult()
    {
        var token = MakeToken();
        var rows = new List<TokenPageRow> { MakeRow(token) };
        var sparkline = new int[24];
        sparkline[23] = 7;
        var sparklines = new Dictionary<Guid, int[]> { [token.Id] = sparkline };

        _tokenRepo.GetPagedWithStatsAsync(0, 50, Arg.Any<CancellationToken>()).Returns((rows, 1));
        _requestRepo.GetSparklineBatchAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(sparklines);

        var result = await CreateHandler().Handle(new GetTokensQuery(), CancellationToken.None);

        result.Items[0].Sparkline24h[23].Should().Be(7);
    }

    [Fact]
    public async Task Handle_ProducesRelativeUrls_WhenBaseUrlIsEmpty()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions { BaseUrl = "", MaxRequestSizeMb = 5, RetentionDays = 7 });
        var handler = new GetTokensQueryHandler(_tokenRepo, _requestRepo, options);
        var token = MakeToken();
        _tokenRepo.GetPagedWithStatsAsync(0, 50, Arg.Any<CancellationToken>())
            .Returns((new List<TokenPageRow> { MakeRow(token) }, 1));
        _requestRepo.GetSparklineBatchAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int[]>());

        var result = await handler.Handle(new GetTokensQuery(), CancellationToken.None);

        result.Items[0].WebhookUrl.Should().StartWith("/webhook/");
    }
}

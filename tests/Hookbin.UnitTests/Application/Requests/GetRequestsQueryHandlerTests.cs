using FluentAssertions;
using NSubstitute;
using Hookbin.Application.Requests.Queries.GetRequests;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Requests;

public sealed class GetRequestsQueryHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private GetRequestsQueryHandler CreateHandler() => new(_repo);

    private static WebhookRequest MakeRequest(Guid tokenId) => new()
    {
        Id = Guid.NewGuid(),
        TokenId = tokenId,
        Method = "POST",
        Path = "/webhook/abc",
        ReceivedAt = DateTimeOffset.UtcNow,
        Headers = "{}",
        IpAddress = "127.0.0.1",
        SizeBytes = 10
    };

    [Fact]
    public async Task Handle_ReturnsPagedResult_WithMappedDtos()
    {
        var tokenId = Guid.NewGuid();
        var items = new List<WebhookRequest> { MakeRequest(tokenId), MakeRequest(tokenId) };
        _repo.GetPagedAsync(tokenId, 1, 10, null, null, null, Arg.Any<CancellationToken>())
            .Returns((items, 2));

        var result = await CreateHandler().Handle(
            new GetRequestsQuery(tokenId, 1, 10, null), CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyPage_WhenNoRequests()
    {
        var tokenId = Guid.NewGuid();
        _repo.GetPagedAsync(tokenId, 1, 10, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<WebhookRequest>(), 0));

        var result = await CreateHandler().Handle(
            new GetRequestsQuery(tokenId, 1, 10, null), CancellationToken.None);

        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesSearchTermToRepository()
    {
        var tokenId = Guid.NewGuid();
        _repo.GetPagedAsync(tokenId, 1, 5, "foo", null, null, Arg.Any<CancellationToken>())
            .Returns((new List<WebhookRequest>(), 0));

        await CreateHandler().Handle(new GetRequestsQuery(tokenId, 1, 5, "foo"), CancellationToken.None);

        await _repo.Received(1).GetPagedAsync(tokenId, 1, 5, "foo", null, null, Arg.Any<CancellationToken>());
    }
}
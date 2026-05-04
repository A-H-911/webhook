using FluentAssertions;
using NSubstitute;
using WebhookService.Application.Requests.Queries.GetRequestById;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Requests;

public sealed class GetRequestByIdQueryHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private GetRequestByIdQueryHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_ReturnsDetail_WhenRequestExistsAndTokenMatches()
    {
        var tokenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = tokenId, Method = "POST", Path = "/hooks/abc",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "127.0.0.1", SizeBytes = 10
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(new GetRequestByIdQuery(tokenId, requestId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(requestId);
        result.Method.Should().Be("POST");
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenRequestNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookRequest?)null);

        var result = await CreateHandler().Handle(
            new GetRequestByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenTokenIdMismatch_PreventingIdor()
    {
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = Guid.NewGuid(), Method = "POST", Path = "/hooks/abc",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "127.0.0.1", SizeBytes = 10
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(
            new GetRequestByIdQuery(Guid.NewGuid(), requestId), CancellationToken.None);

        result.Should().BeNull();
    }
}
using FluentAssertions;
using NSubstitute;
using Hookbin.Application.Requests.Commands.DeleteRequest;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Requests;

public sealed class DeleteRequestCommandHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private DeleteRequestCommandHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_ReturnsTrue_WhenRequestExistsAndTokenMatches()
    {
        var tokenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = tokenId, Method = "POST", Path = "/hooks/x",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "127.0.0.1", SizeBytes = 0
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(new DeleteRequestCommand(tokenId, requestId), CancellationToken.None);

        result.Should().BeTrue();
        await _repo.Received(1).DeleteAsync(requestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFalse_WhenRequestNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookRequest?)null);

        var result = await CreateHandler().Handle(
            new DeleteRequestCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Should().BeFalse();
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFalse_WhenTokenIdMismatch_PreventingIdor()
    {
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = Guid.NewGuid(), Method = "POST", Path = "/hooks/x",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "127.0.0.1", SizeBytes = 0
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(
            new DeleteRequestCommand(Guid.NewGuid(), requestId), CancellationToken.None);

        result.Should().BeFalse();
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
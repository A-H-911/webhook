using FluentAssertions;
using NSubstitute;
using System.Text.Json;
using Hookbin.Application.Requests.Queries.ExportRequest;
using Hookbin.Domain.Entities;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Requests;

public sealed class ExportRequestQueryHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private ExportRequestQueryHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_ReturnsJsonBytes_WhenRequestExistsAndTokenMatches()
    {
        var tokenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = tokenId, Method = "GET", Path = "/hooks/abc",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "10.0.0.1", SizeBytes = 0
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(new ExportRequestQuery(tokenId, requestId), CancellationToken.None);

        result.Should().NotBeNull();
        var json = JsonDocument.Parse(result!);
        json.RootElement.GetProperty("method").GetString().Should().Be("GET");
        json.RootElement.GetProperty("path").GetString().Should().Be("/hooks/abc");
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WebhookRequest?)null);

        var result = await CreateHandler().Handle(
            new ExportRequestQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenTokenIdMismatch_PreventingIdor()
    {
        var requestId = Guid.NewGuid();
        var request = new WebhookRequest
        {
            Id = requestId, TokenId = Guid.NewGuid(), Method = "POST", Path = "/hooks/x",
            ReceivedAt = DateTimeOffset.UtcNow, Headers = "{}", IpAddress = "127.0.0.1", SizeBytes = 5
        };
        _repo.GetByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(
            new ExportRequestQuery(Guid.NewGuid(), requestId), CancellationToken.None);

        result.Should().BeNull();
    }
}
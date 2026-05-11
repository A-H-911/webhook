using NSubstitute;
using Hookbin.Application.Requests.Commands.ClearRequests;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Requests;

public sealed class ClearRequestsCommandHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private ClearRequestsCommandHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_CallsDeleteAllForTokenAsync()
    {
        var tokenId = Guid.NewGuid();

        await CreateHandler().Handle(new ClearRequestsCommand(tokenId), CancellationToken.None);

        await _repo.Received(1).DeleteAllForTokenAsync(tokenId, Arg.Any<CancellationToken>());
    }
}
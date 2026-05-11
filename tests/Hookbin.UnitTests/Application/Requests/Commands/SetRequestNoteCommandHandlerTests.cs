using FluentAssertions;
using NSubstitute;
using Hookbin.Application.Requests.Commands.SetRequestNote;
using Hookbin.Domain.Repositories;

namespace Hookbin.UnitTests.Application.Requests.Commands;

public sealed class SetRequestNoteCommandHandlerTests
{
    private readonly IWebhookRequestRepository _repo = Substitute.For<IWebhookRequestRepository>();

    private SetRequestNoteCommandHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_ReturnsTrue_WhenRepositoryFindsAndUpdatesRequest()
    {
        var tokenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        _repo.UpdateNoteAsync(requestId, tokenId, "my note", Arg.Any<CancellationToken>())
             .Returns(true);

        var result = await CreateHandler().Handle(
            new SetRequestNoteCommand(tokenId, requestId, "my note"), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFalse_WhenRepositoryFindsNoMatchingRequest()
    {
        _repo.UpdateNoteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(false);

        var result = await CreateHandler().Handle(
            new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), "note"), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PassesNullNote_WhenNoteIsClearedWithNull()
    {
        var tokenId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        _repo.UpdateNoteAsync(requestId, tokenId, null, Arg.Any<CancellationToken>())
             .Returns(true);

        await CreateHandler().Handle(
            new SetRequestNoteCommand(tokenId, requestId, null), CancellationToken.None);

        await _repo.Received(1).UpdateNoteAsync(requestId, tokenId, null, Arg.Any<CancellationToken>());
    }
}

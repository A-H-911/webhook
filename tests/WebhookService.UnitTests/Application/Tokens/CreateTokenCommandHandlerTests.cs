using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookService.Application.Options;
using WebhookService.Application.Tokens.Commands.CreateToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.Repositories;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class CreateTokenCommandHandlerTests
{
    private readonly IWebhookTokenRepository _repo = Substitute.For<IWebhookTokenRepository>();
    private readonly IOptions<WebhookOptions> _options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
    {
        BaseUrl = "https://example.com",
        RetentionDays = 7,
        MaxRequestSizeMb = 5
    });

    private CreateTokenCommandHandler CreateHandler() => new(_repo, _options);

    [Fact]
    public async Task Handle_CreatesToken_AndReturnsDto()
    {
        var command = new CreateTokenCommand("my description");
        WebhookToken? captured = null;
        await _repo.AddAsync(Arg.Do<WebhookToken>(t => captured = t), Arg.Any<CancellationToken>());

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.WebhookUrl.Should().Contain("https://example.com/webhook/");
        result.Description.Should().Be("my description");
        result.IsActive.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Id.Should().NotBe(Guid.Empty);
        captured.Token.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WithNullDescription_CreatesToken()
    {
        var command = new CreateTokenCommand(null);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Description.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TrimsTrailingSlashFromBaseUrl()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WebhookOptions
        {
            BaseUrl = "https://example.com/",
            RetentionDays = 7,
            MaxRequestSizeMb = 5
        });
        var handler = new CreateTokenCommandHandler(_repo, options);

        var result = await handler.Handle(new CreateTokenCommand(null), CancellationToken.None);

        result.WebhookUrl.Should().NotContain("//webhook/");
        result.WebhookUrl.Should().Contain("https://example.com/webhook/");
    }

    [Fact]
    public async Task Handle_CallsRepositoryAddAsync_Once()
    {
        await CreateHandler().Handle(new CreateTokenCommand("test"), CancellationToken.None);

        await _repo.Received(1).AddAsync(Arg.Any<WebhookToken>(), Arg.Any<CancellationToken>());
    }
}

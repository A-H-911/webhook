using FluentAssertions;
using WebhookService.Application.Tokens.Queries.GetToken;
using WebhookService.Domain.Entities;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class WebhookTokenExtensionsTests
{
    private static WebhookToken MakeToken(bool withCustomResponse = false) => new()
    {
        Id = Guid.NewGuid(),
        Token = Guid.NewGuid(),
        Description = "test token",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        CustomResponse = withCustomResponse
            ? new CustomResponse
            {
                StatusCode = 201,
                ContentType = "application/json",
                Body = "{\"ok\":true}",
                Headers = "{}"
            }
            : null
    };

    [Fact]
    public void ToDto_MapsAllFields_WhenNoCustomResponse()
    {
        var token = MakeToken();

        var dto = token.ToDto("https://example.com");

        dto.Id.Should().Be(token.Id);
        dto.Token.Should().Be(token.Token);
        dto.Description.Should().Be(token.Description);
        dto.IsActive.Should().BeTrue();
        dto.WebhookUrl.Should().Be($"https://example.com/webhook/{token.Token}");
        dto.CustomResponse.Should().BeNull();
    }

    [Fact]
    public void ToDto_MapsCustomResponse_WhenPresent()
    {
        var token = MakeToken(withCustomResponse: true);

        var dto = token.ToDto("https://example.com");

        dto.CustomResponse.Should().NotBeNull();
        dto.CustomResponse!.StatusCode.Should().Be(201);
        dto.CustomResponse.ContentType.Should().Be("application/json");
        dto.CustomResponse.Body.Should().Be("{\"ok\":true}");
        dto.CustomResponse.Headers.Should().Be("{}");
    }

    [Fact]
    public void ToDto_TrimsTrailingSlash_FromBaseUrl()
    {
        var token = MakeToken();

        var dto = token.ToDto("https://example.com/");

        dto.WebhookUrl.Should().StartWith("https://example.com/webhook/");
        dto.WebhookUrl.Should().NotContain("//webhook/");
    }

    [Fact]
    public void ToDto_BuildsCorrectWebhookUrl()
    {
        var token = MakeToken();

        var dto = token.ToDto("https://hooks.example.com");

        dto.WebhookUrl.Should().Be($"https://hooks.example.com/webhook/{token.Token}");
    }

    [Fact]
    public void ToDto_NullCustomResponse_WhenTokenHasNone()
    {
        var token = MakeToken(withCustomResponse: false);

        var dto = token.ToDto("https://example.com");

        dto.CustomResponse.Should().BeNull();
    }
}

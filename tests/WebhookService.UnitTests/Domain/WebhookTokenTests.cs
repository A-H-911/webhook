using FluentAssertions;
using WebhookService.Domain.Entities;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.Domain;

public sealed class WebhookTokenTests
{
    [Fact]
    public void Token_ShouldBeNewGuid_WhenCreated()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.Token.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void IsActive_ShouldDefaultToTrue()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_CanBeSetToFalse()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.Deactivate();

        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Description_ShouldBeNullByDefault()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.Description.Should().BeNull();
    }

    [Fact]
    public void CustomResponse_ShouldBeNullByDefault()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.CustomResponse.Should().BeNull();
    }

    [Fact]
    public void CustomResponse_CanBeAssigned()
    {
        var response = new CustomResponse { StatusCode = 201, ContentType = "application/json", Body = "{}" };
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.SetCustomResponse(response);

        token.CustomResponse.Should().NotBeNull();
        token.CustomResponse!.StatusCode.Should().Be(201);
        token.CustomResponse.ContentType.Should().Be("application/json");
        token.CustomResponse.Body.Should().Be("{}");
    }
}

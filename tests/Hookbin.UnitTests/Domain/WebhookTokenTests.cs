using FluentAssertions;
using Hookbin.Domain.Entities;
using Hookbin.Domain.ValueObjects;

namespace Hookbin.UnitTests.Domain;

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
    public void Name_DefaultsToEmpty()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.Name.Should().BeEmpty();
    }

    [Fact]
    public void UpdateName_SetsName_WhenValid()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.UpdateName("github-events");

        token.Name.Should().Be("github-events");
    }

    [Fact]
    public void UpdateName_TrimsWhitespace()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.UpdateName("  my-hook  ");

        token.Name.Should().Be("my-hook");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateName_Throws_WhenNameIsNullOrWhitespace(string name)
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        var act = () => token.UpdateName(name);

        act.Should().Throw<ArgumentException>().WithMessage("*required*");
    }

    [Fact]
    public void UpdateName_Throws_WhenNameExceeds80Characters()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var longName = new string('a', 81);

        var act = () => token.UpdateName(longName);

        act.Should().Throw<ArgumentException>().WithMessage("*80*");
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

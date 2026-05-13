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

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.Deactivate();
        token.Activate();

        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Activate_IsIdempotent()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.Activate();
        token.Activate();

        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.Deactivate();
        token.Deactivate();

        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UpdateDescription_AcceptsNull()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.UpdateDescription("first");
        token.UpdateDescription(null);

        token.Description.Should().BeNull();
    }

    [Fact]
    public void UpdateDescription_DoesNotTrim()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.UpdateDescription("  description with spaces  ");

        token.Description.Should().Be("  description with spaces  ");
    }

    [Fact]
    public void UpdateDescription_AcceptsEmpty()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.UpdateDescription(string.Empty);

        token.Description.Should().Be(string.Empty);
    }

    [Fact]
    public void ClearCustomResponse_SetsCustomResponseToNull()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.SetCustomResponse(new CustomResponse { StatusCode = 200, ContentType = "text/plain" });
        token.ClearCustomResponse();

        token.CustomResponse.Should().BeNull();
    }

    [Fact]
    public void ClearCustomResponse_IsSafe_WhenAlreadyNull()
    {
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        var act = token.ClearCustomResponse;

        act.Should().NotThrow();
        token.CustomResponse.Should().BeNull();
    }

    [Fact]
    public void SetCustomResponse_PreservesReferenceEquality()
    {
        var response = new CustomResponse { StatusCode = 202, ContentType = "text/plain" };
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        token.SetCustomResponse(response);

        token.CustomResponse.Should().BeSameAs(response);
    }

    [Fact]
    public void SetCustomResponse_ThenClearThenSet_RetainsLatest()
    {
        var first = new CustomResponse { StatusCode = 200, ContentType = "text/plain" };
        var second = new CustomResponse { StatusCode = 418, ContentType = "application/json", Body = "teapot" };
        var token = new WebhookToken { Id = Guid.NewGuid(), Token = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        token.SetCustomResponse(first);
        token.ClearCustomResponse();
        token.SetCustomResponse(second);

        token.CustomResponse.Should().BeSameAs(second);
        token.CustomResponse!.StatusCode.Should().Be(418);
    }
}

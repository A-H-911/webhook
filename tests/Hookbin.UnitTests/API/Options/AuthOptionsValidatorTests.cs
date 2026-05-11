using FluentAssertions;
using Microsoft.Extensions.Options;
using Hookbin.API.Options;

namespace Hookbin.UnitTests.API.Options;

public sealed class AuthOptionsValidatorTests
{
    private readonly AuthOptionsValidator _validator = new();

    private static AuthOptions Valid() => new()
    {
        Username = "admin",
        PasswordHash = "$2b$12$abcdefghijklmnopqrstuvuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuu",
        SessionHours = 8
    };

    [Fact]
    public void Validate_Succeeds_WithValidOptions()
    {
        var result = _validator.Validate(null, Valid());

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_WhenUsernameIsEmpty(string username)
    {
        var opts = new AuthOptions { Username = username, PasswordHash = "$2b$12$abc", SessionHours = 8 };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Username");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plaintextpassword")]
    [InlineData("$1$notatbcrypt")]
    public void Validate_Fails_WhenPasswordHashInvalid(string hash)
    {
        var opts = new AuthOptions { Username = "admin", PasswordHash = hash, SessionHours = 8 };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BCrypt");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    [InlineData(-1)]
    public void Validate_Fails_WhenSessionHoursOutOfRange(int hours)
    {
        var opts = new AuthOptions { Username = "admin", PasswordHash = "$2b$12$abc", SessionHours = hours };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SessionHours");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(168)]
    public void Validate_Succeeds_WhenSessionHoursAtBoundary(int hours)
    {
        var opts = new AuthOptions { Username = "admin", PasswordHash = "$2b$12$abc", SessionHours = hours };

        var result = _validator.Validate(null, opts);

        result.Should().Be(ValidateOptionsResult.Success);
    }
}

using FluentAssertions;
using Hookbin.Application.Tokens.Commands.CreateToken;

namespace Hookbin.UnitTests.Application.Tokens;

public sealed class CreateTokenCommandValidatorTests
{
    private readonly CreateTokenCommandValidator _validator = new();

    [Fact]
    public void Validate_Passes_WhenNameAndDescriptionAreValid()
    {
        var result = _validator.Validate(new CreateTokenCommand("github-events", "GitHub webhooks"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsNull()
    {
        var result = _validator.Validate(new CreateTokenCommand("github-events", null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsWithinLimit()
    {
        var result = _validator.Validate(new CreateTokenCommand("valid-name", new string('x', 200)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenNameIsEmpty()
    {
        var result = _validator.Validate(new CreateTokenCommand("", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_Fails_WhenNameExceeds80Characters()
    {
        var result = _validator.Validate(new CreateTokenCommand(new string('a', 81), null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_Fails_WhenDescriptionExceeds200Chars()
    {
        var result = _validator.Validate(new CreateTokenCommand("valid-name", new string('x', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Description");
    }
}

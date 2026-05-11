using FluentAssertions;
using Hookbin.Application.Tokens.Commands.UpdateToken;

namespace Hookbin.UnitTests.Application.Tokens;

public sealed class UpdateTokenCommandValidatorTests
{
    private readonly UpdateTokenCommandValidator _validator = new();

    [Fact]
    public void Validate_Passes_WhenNameAndDescriptionAreValid()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "github-events", "desc", true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsNull()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "valid-name", null, true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsWithinLimit()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "valid-name", new string('x', 200), true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsEmpty()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "valid-name", string.Empty, false));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenNameIsEmpty()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "", null, true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_Fails_WhenNameExceeds80Characters()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), new string('a', 81), null, true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_Fails_WhenDescriptionExceeds200Chars()
    {
        var result = _validator.Validate(new UpdateTokenCommand(Guid.NewGuid(), "valid-name", new string('x', 201), true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Description");
    }
}

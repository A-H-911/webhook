using FluentAssertions;
using WebhookService.Application.Tokens.Commands.CreateToken;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class CreateTokenCommandValidatorTests
{
    private readonly CreateTokenCommandValidator _validator = new();

    [Fact]
    public void Validate_Passes_WhenDescriptionIsNull()
    {
        var result = _validator.Validate(new CreateTokenCommand(null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsWithinLimit()
    {
        var result = _validator.Validate(new CreateTokenCommand(new string('x', 200)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenDescriptionIsEmpty()
    {
        var result = _validator.Validate(new CreateTokenCommand(string.Empty));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenDescriptionExceeds200Chars()
    {
        var result = _validator.Validate(new CreateTokenCommand(new string('x', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Description");
    }
}

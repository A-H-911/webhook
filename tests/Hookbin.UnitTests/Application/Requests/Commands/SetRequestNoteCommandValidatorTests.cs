using FluentAssertions;
using FluentValidation.TestHelper;
using Hookbin.Application.Requests.Commands.SetRequestNote;

namespace Hookbin.UnitTests.Application.Requests.Commands;

public sealed class SetRequestNoteCommandValidatorTests
{
    private readonly SetRequestNoteCommandValidator _sut = new();

    [Fact]
    public void Validate_IsValid_WhenNoteIsNull()
    {
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_IsValid_WhenNoteIsEmptyString()
    {
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), ""));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_IsValid_WhenNoteIsWhitespaceOnly()
    {
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), "   "));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_IsValid_WhenNoteIsExactly2000Chars()
    {
        var note = new string('x', 2000);
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), note));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_IsInvalid_WhenNoteExceeds2000Chars()
    {
        var note = new string('x', 2001);
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.NewGuid(), note));
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(c => c.Note);
    }

    [Fact]
    public void Validate_IsInvalid_WhenTokenIdIsEmpty()
    {
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.Empty, Guid.NewGuid(), "a note"));
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(c => c.TokenId);
    }

    [Fact]
    public void Validate_IsInvalid_WhenRequestIdIsEmpty()
    {
        var result = _sut.TestValidate(new SetRequestNoteCommand(Guid.NewGuid(), Guid.Empty, "a note"));
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(c => c.RequestId);
    }
}

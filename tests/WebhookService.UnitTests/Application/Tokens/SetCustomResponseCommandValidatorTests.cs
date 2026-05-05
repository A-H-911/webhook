using FluentAssertions;
using WebhookService.Application.Tokens.Commands.SetCustomResponse;

namespace WebhookService.UnitTests.Application.Tokens;

public sealed class SetCustomResponseCommandValidatorTests
{
    private readonly SetCustomResponseCommandValidator _validator = new();

    [Theory]
    [InlineData(200, "application/json", "{}", true)]
    [InlineData(100, "text/plain", "{}", true)]
    [InlineData(599, "text/plain", "{}", true)]
    [InlineData(99, "text/plain", "{}", false)]
    [InlineData(600, "text/plain", "{}", false)]
    [InlineData(200, "", "{}", false)]
    [InlineData(200, "text/plain", "not-json", false)]
    [InlineData(200, "text/plain", "[1,2]", false)]
    [InlineData(200, "text/plain", null, false)]
    public void Validate_ReturnsExpectedResult(
        int statusCode, string contentType, string? headers, bool isValid)
    {
        var cmd = new SetCustomResponseCommand(Guid.NewGuid(), statusCode, contentType, null, headers!);

        var result = _validator.Validate(cmd);

        result.IsValid.Should().Be(isValid);
    }

    [Fact]
    public void Validate_FailsWithMessage_ForInvalidStatusCode()
    {
        var cmd = new SetCustomResponseCommand(Guid.NewGuid(), 99, "text/plain", null, "{}");

        var result = _validator.Validate(cmd);

        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "StatusCode" && e.ErrorMessage.Contains("100"));
    }

    [Fact]
    public void Validate_FailsWithMessage_ForInvalidJsonHeaders()
    {
        var cmd = new SetCustomResponseCommand(Guid.NewGuid(), 200, "text/plain", null, "not-json");

        var result = _validator.Validate(cmd);

        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "Headers" && e.ErrorMessage.Contains("JSON"));
    }
}
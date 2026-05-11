using FluentAssertions;
using Microsoft.Extensions.Options;
using Hookbin.API.Options;
using Hookbin.Application.Options;

namespace Hookbin.UnitTests.Application.Options;

public sealed class WebhookOptionsValidatorTests
{
    private readonly WebhookOptionsValidator _validator = new();

    private static WebhookOptions Valid() => new()
    {
        BaseUrl = "https://example.com",
        MaxRequestSizeMb = 5,
        RetentionDays = 7
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
    public void Validate_Fails_WhenBaseUrlIsEmpty(string baseUrl)
    {
        var opts = new WebhookOptions { BaseUrl = baseUrl, MaxRequestSizeMb = 5, RetentionDays = 7 };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BaseUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_Fails_WhenMaxRequestSizeMbOutOfRange(int mb)
    {
        var opts = new WebhookOptions { BaseUrl = "https://example.com", MaxRequestSizeMb = mb, RetentionDays = 7 };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxRequestSizeMb");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_Succeeds_WhenMaxRequestSizeMbAtBoundary(int mb)
    {
        var opts = new WebhookOptions { BaseUrl = "https://example.com", MaxRequestSizeMb = mb, RetentionDays = 7 };

        var result = _validator.Validate(null, opts);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void Validate_Fails_WhenRetentionDaysOutOfRange(int days)
    {
        var opts = new WebhookOptions { BaseUrl = "https://example.com", MaxRequestSizeMb = 5, RetentionDays = days };

        var result = _validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RetentionDays");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(365)]
    public void Validate_Succeeds_WhenRetentionDaysAtBoundary(int days)
    {
        var opts = new WebhookOptions { BaseUrl = "https://example.com", MaxRequestSizeMb = 5, RetentionDays = days };

        var result = _validator.Validate(null, opts);

        result.Should().Be(ValidateOptionsResult.Success);
    }
}

using FluentAssertions;
using WebhookService.Application.Requests.Queries.GetRequests;

namespace WebhookService.UnitTests.Application.Requests;

public sealed class GetRequestsQueryValidatorTests
{
    private readonly GetRequestsQueryValidator _validator = new();

    private static GetRequestsQuery Valid(int page = 1, int pageSize = 20, string? search = null)
        => new(Guid.NewGuid(), page, pageSize, search);

    [Fact]
    public void Validate_Passes_WithDefaults()
    {
        var result = _validator.Validate(Valid());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Fails_WhenPageLessThan1(int page)
    {
        var result = _validator.Validate(Valid(page: page));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_Fails_WhenPageSizeOutOfRange(int pageSize)
    {
        var result = _validator.Validate(Valid(pageSize: pageSize));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "PageSize");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_Passes_WhenPageSizeWithinRange(int pageSize)
    {
        var result = _validator.Validate(Valid(pageSize: pageSize));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenSearchIsNull()
    {
        var result = _validator.Validate(Valid(search: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenSearchIsWithinLimit()
    {
        var result = _validator.Validate(Valid(search: new string('q', 200)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenSearchExceeds200Chars()
    {
        var result = _validator.Validate(Valid(search: new string('q', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Search");
    }
}

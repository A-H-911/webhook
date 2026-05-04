using FluentAssertions;
using WebhookService.Domain.ValueObjects;

namespace WebhookService.UnitTests.Domain;

public sealed class CustomResponseTests
{
    [Fact]
    public void StatusCode_ShouldDefault_To200()
    {
        var response = new CustomResponse();

        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public void ContentType_ShouldDefault_ToTextPlain()
    {
        var response = new CustomResponse();

        response.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public void Headers_ShouldDefault_ToEmptyJsonObject()
    {
        var response = new CustomResponse();

        response.Headers.Should().Be("{}");
    }

    [Fact]
    public void Body_ShouldDefault_ToNull()
    {
        var response = new CustomResponse();

        response.Body.Should().BeNull();
    }

    [Fact]
    public void StatusCode_CanBeSetToCustomValue()
    {
        var response = new CustomResponse { StatusCode = 404 };

        response.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ContentType_CanBeSetToCustomValue()
    {
        var response = new CustomResponse { ContentType = "application/json" };

        response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void Headers_CanStoreJsonString()
    {
        const string headers = """{"X-Custom":"value"}""";
        var response = new CustomResponse { Headers = headers };

        response.Headers.Should().Be(headers);
    }
}

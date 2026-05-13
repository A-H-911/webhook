using FluentAssertions;
using Hookbin.Domain.Entities;

namespace Hookbin.UnitTests.Domain;

public sealed class WebhookRequestTests
{
    private static WebhookRequest Build(string method = "POST", string path = "/hooks/abc")
        => new()
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = method,
            Path = path,
            Headers = "{}",
            IpAddress = "127.0.0.1"
        };

    [Fact]
    public void Method_ShouldRetainAssignedValue()
    {
        var request = Build(method: "GET");

        request.Method.Should().Be("GET");
    }

    [Fact]
    public void IsBodyBase64_ShouldDefaultToFalse()
    {
        var request = Build();

        request.IsBodyBase64.Should().BeFalse();
    }

    [Fact]
    public void IpAddress_ShouldDefaultToUnknown_WhenNotSet()
    {
        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            Headers = "{}"
        };

        request.IpAddress.Should().Be("unknown");
    }

    [Fact]
    public void Headers_ShouldDefaultToEmptyJsonObject_WhenNotSet()
    {
        var request = new WebhookRequest
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            IpAddress = "1.2.3.4"
        };

        request.Headers.Should().Be("{}");
    }

    [Fact]
    public void NullableFields_ShouldBeNullByDefault()
    {
        var request = Build();

        request.QueryString.Should().BeNull();
        request.Body.Should().BeNull();
        request.ContentType.Should().BeNull();
        request.UserAgent.Should().BeNull();
    }

    [Fact]
    public void SizeBytes_ShouldDefaultToZero()
    {
        var request = Build();

        request.SizeBytes.Should().Be(0);
    }

    [Fact]
    public void TokenNavigation_ShouldBeNullByDefault()
    {
        var request = Build();

        request.Token.Should().BeNull();
    }

    [Fact]
    public void ProcessingTimeMs_ShouldBeNullByDefault()
    {
        var request = Build();

        request.ProcessingTimeMs.Should().BeNull();
    }

    [Fact]
    public void RecordProcessingTime_StoresPositiveValue()
    {
        var request = Build();

        request.RecordProcessingTime(50);

        request.ProcessingTimeMs.Should().Be(50);
    }

    [Fact]
    public void RecordProcessingTime_StoresZero()
    {
        var request = Build();

        request.RecordProcessingTime(0);

        request.ProcessingTimeMs.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    [InlineData(long.MinValue)]
    public void RecordProcessingTime_ClampsNegativeToZero(long input)
    {
        var request = Build();

        request.RecordProcessingTime(input);

        request.ProcessingTimeMs.Should().Be(0);
    }

    [Fact]
    public void RecordProcessingTime_PreservesLargePositiveValues()
    {
        var request = Build();

        request.RecordProcessingTime(long.MaxValue);

        request.ProcessingTimeMs.Should().Be(long.MaxValue);
    }

    [Fact]
    public void RecordProcessingTime_LatestCallWins()
    {
        var request = Build();

        request.RecordProcessingTime(10);
        request.RecordProcessingTime(-5);

        request.ProcessingTimeMs.Should().Be(0);
    }

    [Fact]
    public void RecordProcessingTime_OverwritesPreviousValue()
    {
        var request = Build();

        request.RecordProcessingTime(100);
        request.RecordProcessingTime(200);

        request.ProcessingTimeMs.Should().Be(200);
    }
}

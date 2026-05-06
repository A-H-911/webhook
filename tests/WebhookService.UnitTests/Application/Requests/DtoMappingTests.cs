using FluentAssertions;
using WebhookService.Application.Requests.Queries.GetRequestById;
using WebhookService.Application.Requests.Queries.GetRequests;

namespace WebhookService.UnitTests.Application.Requests;

/// <summary>
/// Exercises all properties of the DTO record types to ensure the coverage
/// instrument records every field accessor as hit.
/// </summary>
public sealed class DtoMappingTests
{
    // ── WebhookRequestSummaryDto ──────────────────────────────────────────────

    [Fact]
    public void WebhookRequestSummaryDto_AllProperties_AreAccessible()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;

        // Act
        var dto = new WebhookRequestSummaryDto(
            Id: id,
            TokenId: tokenId,
            Method: "POST",
            Path: "/webhook/abc",
            ReceivedAt: receivedAt,
            ContentType: "application/json",
            SizeBytes: 512,
            IpAddress: "10.0.0.1");

        // Assert
        dto.Id.Should().Be(id);
        dto.TokenId.Should().Be(tokenId);
        dto.Method.Should().Be("POST");
        dto.Path.Should().Be("/webhook/abc");
        dto.ReceivedAt.Should().Be(receivedAt);
        dto.ContentType.Should().Be("application/json");
        dto.SizeBytes.Should().Be(512);
        dto.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public void WebhookRequestSummaryDto_NullableContentType_AcceptsNull()
    {
        // Arrange & Act
        var dto = new WebhookRequestSummaryDto(
            Id: Guid.NewGuid(),
            TokenId: Guid.NewGuid(),
            Method: "GET",
            Path: "/webhook/xyz",
            ReceivedAt: DateTimeOffset.UtcNow,
            ContentType: null,
            SizeBytes: 0,
            IpAddress: "127.0.0.1");

        // Assert
        dto.ContentType.Should().BeNull();
    }

    [Fact]
    public void WebhookRequestSummaryDto_EqualityByValue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;

        var a = new WebhookRequestSummaryDto(id, tokenId, "POST", "/hook", receivedAt, "text/plain", 10, "1.2.3.4");
        var b = new WebhookRequestSummaryDto(id, tokenId, "POST", "/hook", receivedAt, "text/plain", 10, "1.2.3.4");

        // Assert — record structural equality
        a.Should().Be(b);
    }

    [Fact]
    public void WebhookRequestSummaryDto_InequalityWhenMethodDiffers()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;

        var a = new WebhookRequestSummaryDto(id, tokenId, "POST", "/hook", receivedAt, null, 0, "1.1.1.1");
        var b = new WebhookRequestSummaryDto(id, tokenId, "GET", "/hook", receivedAt, null, 0, "1.1.1.1");

        // Assert
        a.Should().NotBe(b);
    }

    // ── WebhookRequestDetailDto ───────────────────────────────────────────────

    [Fact]
    public void WebhookRequestDetailDto_AllProperties_AreAccessible()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;

        // Act
        var dto = new WebhookRequestDetailDto(
            Id: id,
            TokenId: tokenId,
            Method: "PUT",
            Path: "/webhook/detail",
            QueryString: "?foo=bar",
            ReceivedAt: receivedAt,
            ContentType: "application/json",
            Headers: "{\"X-Foo\":\"bar\"}",
            Body: "{\"key\":\"value\"}",
            IsBodyBase64: false,
            SizeBytes: 1024,
            IpAddress: "192.168.1.1",
            UserAgent: "TestAgent/1.0");

        // Assert
        dto.Id.Should().Be(id);
        dto.TokenId.Should().Be(tokenId);
        dto.Method.Should().Be("PUT");
        dto.Path.Should().Be("/webhook/detail");
        dto.QueryString.Should().Be("?foo=bar");
        dto.ReceivedAt.Should().Be(receivedAt);
        dto.ContentType.Should().Be("application/json");
        dto.Headers.Should().Be("{\"X-Foo\":\"bar\"}");
        dto.Body.Should().Be("{\"key\":\"value\"}");
        dto.IsBodyBase64.Should().BeFalse();
        dto.SizeBytes.Should().Be(1024);
        dto.IpAddress.Should().Be("192.168.1.1");
        dto.UserAgent.Should().Be("TestAgent/1.0");
    }

    [Fact]
    public void WebhookRequestDetailDto_NullableFields_AcceptNull()
    {
        // Arrange & Act
        var dto = new WebhookRequestDetailDto(
            Id: Guid.NewGuid(),
            TokenId: Guid.NewGuid(),
            Method: "GET",
            Path: "/hook",
            QueryString: null,
            ReceivedAt: DateTimeOffset.UtcNow,
            ContentType: null,
            Headers: "{}",
            Body: null,
            IsBodyBase64: false,
            SizeBytes: 0,
            IpAddress: "127.0.0.1",
            UserAgent: null);

        // Assert
        dto.QueryString.Should().BeNull();
        dto.ContentType.Should().BeNull();
        dto.Body.Should().BeNull();
        dto.UserAgent.Should().BeNull();
    }

    [Fact]
    public void WebhookRequestDetailDto_IsBodyBase64_True_WhenSet()
    {
        // Arrange & Act
        var dto = new WebhookRequestDetailDto(
            Id: Guid.NewGuid(),
            TokenId: Guid.NewGuid(),
            Method: "POST",
            Path: "/hook",
            QueryString: null,
            ReceivedAt: DateTimeOffset.UtcNow,
            ContentType: "application/octet-stream",
            Headers: "{}",
            Body: "SGVsbG8=",
            IsBodyBase64: true,
            SizeBytes: 5,
            IpAddress: "10.0.0.1",
            UserAgent: null);

        // Assert
        dto.IsBodyBase64.Should().BeTrue();
        dto.Body.Should().Be("SGVsbG8=");
    }

    [Fact]
    public void WebhookRequestDetailDto_EqualityByValue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;

        var a = new WebhookRequestDetailDto(id, tokenId, "GET", "/x", null, receivedAt, null, "{}", null, false, 0, "1.1.1.1", null);
        var b = new WebhookRequestDetailDto(id, tokenId, "GET", "/x", null, receivedAt, null, "{}", null, false, 0, "1.1.1.1", null);

        // Assert
        a.Should().Be(b);
    }

    // ── PagedResult<T> ────────────────────────────────────────────────────────

    [Fact]
    public void PagedResult_AllProperties_AreAccessible()
    {
        // Arrange
        var items = new List<WebhookRequestSummaryDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "POST", "/hook", DateTimeOffset.UtcNow, null, 0, "127.0.0.1")
        };

        // Act
        var result = new PagedResult<WebhookRequestSummaryDto>(items, Total: 42, Page: 3, PageSize: 10);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Total.Should().Be(42);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public void PagedResult_EmptyItems_IsValid()
    {
        // Arrange & Act
        var result = new PagedResult<WebhookRequestSummaryDto>(
            Items: Array.Empty<WebhookRequestSummaryDto>(),
            Total: 0,
            Page: 1,
            PageSize: 10);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }
}

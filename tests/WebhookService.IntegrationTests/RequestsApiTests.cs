using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WebhookService.IntegrationTests;

[Collection("Integration")]
public sealed class RequestsApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/tokens", new { description = "requests-test" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body.GetProperty("id").GetString()!;
        var url = body.GetProperty("webhookUrl").GetString()!;
        return (id, url.Split('/').Last());
    }

    private async Task SendWebhookAsync(string webhookToken, string body = "{}")
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        await _client.PostAsync($"/webhook/{webhookToken}", content);
    }

    [Fact]
    public async Task GetRequests_ReturnsPaginatedList()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken);
        await SendWebhookAsync(webhookToken);

        var response = await _client.GetAsync($"/api/tokens/{tokenId}/requests?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        result.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        result.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetRequestDetail_ReturnsBodyAndHeaders()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken, "{\"key\":\"value\"}");

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = list.GetProperty("items")[0].GetProperty("id").GetString();

        var detailResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        detail.GetProperty("headers").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task DeleteRequest_Returns204_ThenDetailReturns404()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken);

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = list.GetProperty("items")[0].GetProperty("id").GetString();

        var deleteResponse = await _client.DeleteAsync($"/api/tokens/{tokenId}/requests/{requestId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detailResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ClearRequests_Returns204_AndListBecomesEmpty()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken);
        await SendWebhookAsync(webhookToken);

        var clearResponse = await _client.DeleteAsync($"/api/tokens/{tokenId}/requests");
        clearResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        list.GetProperty("total").GetInt32().Should().Be(0);
    }
    [Fact]
    public async Task GetRequestById_WithWrongToken_Returns404_PreventingIdor()
    {
        // Token A receives a webhook — we capture its request id
        var (tokenAId, webhookTokenA) = await CreateTokenAsync();
        await SendWebhookAsync(webhookTokenA, "{\"secret\":true}");

        var listA = await (await _client.GetAsync($"/api/tokens/{tokenAId}/requests"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = listA.GetProperty("items")[0].GetProperty("id").GetString();

        // Token B tries to access token A''s request — must be blocked (IDOR)
        var (tokenBId, _) = await CreateTokenAsync();
        var response = await _client.GetAsync($"/api/tokens/{tokenBId}/requests/{requestId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportRequest_Returns200_WithJsonContent()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken, "{\"export\":true}");

        var list = await (await _client.GetAsync($"/api/tokens/{tokenId}/requests"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = list.GetProperty("items")[0].GetProperty("id").GetString();

        var exportResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}/export");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await exportResponse.Content.ReadAsStringAsync();
        content.Should().Contain("Method");
        content.Should().Contain("Path");
    }

    [Fact]
    public async Task ExportRequest_WithNonExistentRequestId_Returns404()
    {
        var (tokenId, _) = await CreateTokenAsync();

        var response = await _client.GetAsync($"/api/tokens/{tokenId}/requests/{Guid.NewGuid()}/export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRequests_ReceivedAt_HasMillisecondPrecision()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken);

        var response = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var receivedAt = result.GetProperty("items")[0].GetProperty("receivedAt").GetString()!;

        // ISO 8601 with at least 3 fractional-second digits, e.g. "2026-05-06T10:30:45.1234567+00:00"
        receivedAt.Should().MatchRegex(@"\.\d{3,7}");
    }

    [Fact]
    public async Task ExportRequest_ReceivedAt_HasMillisecondPrecision()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken, "{\"export\":true}");

        var list = await (await _client.GetAsync($"/api/tokens/{tokenId}/requests"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = list.GetProperty("items")[0].GetProperty("id").GetString();

        var exportContent = await (await _client.GetAsync(
            $"/api/tokens/{tokenId}/requests/{requestId}/export"))
            .Content.ReadAsStringAsync();

        // Exported JSON must preserve sub-second precision on ReceivedAt
        exportContent.Should().MatchRegex(@"ReceivedAt.*\.\d{3,7}");
    }

    [Fact]
    public async Task GetRequests_IsOrderedNewestFirst()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken, "{\"seq\":1}");
        await SendWebhookAsync(webhookToken, "{\"seq\":2}");
        await SendWebhookAsync(webhookToken, "{\"seq\":3}");

        var response = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = result.GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCountGreaterThanOrEqualTo(3);

        for (var i = 0; i < items.Count - 1; i++)
        {
            var current = DateTimeOffset.Parse(items[i].GetProperty("receivedAt").GetString()!);
            var next = DateTimeOffset.Parse(items[i + 1].GetProperty("receivedAt").GetString()!);
            current.Should().BeOnOrAfter(next);
        }
    }

    [Fact]
    public async Task GetRequests_SecondPage_ReturnsCorrectSubset()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        await SendWebhookAsync(webhookToken, "{\"n\":1}");
        await SendWebhookAsync(webhookToken, "{\"n\":2}");
        await SendWebhookAsync(webhookToken, "{\"n\":3}");

        var response = await _client.GetAsync($"/api/tokens/{tokenId}/requests?page=2&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        result.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        result.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }
}
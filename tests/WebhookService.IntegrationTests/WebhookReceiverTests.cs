using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WebhookService.IntegrationTests;

[Collection("Integration")]
public sealed class WebhookReceiverTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/tokens", new { description = "receiver-test" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body.GetProperty("id").GetString()!;
        var url = body.GetProperty("webhookUrl").GetString()!;
        var token = url.Split('/').Last();
        return (id, token);
    }

    [Fact]
    public async Task PostToWebhook_Returns200_AndCreatesRequest()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        var content = new StringContent("{\"event\":\"test\"}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/webhook/{webhookToken}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        list.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PostToWebhook_WithUnknownToken_Returns404()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/webhook/{Guid.NewGuid()}", content);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostToWebhook_WithCustomResponse_ReturnsConfiguredStatus()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 202,
            contentType = "application/json",
            body = "{\"queued\":true}",
            headers = "{}"
        });

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/webhook/{webhookToken}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetToWebhook_StoresCorrectMethod()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        await _client.GetAsync($"/webhook/{webhookToken}?foo=bar");

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items[0].GetProperty("method").GetString().Should().Be("GET");
    }
    [Fact]
    public async Task PostToWebhook_WithInactiveToken_Returns404()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/webhook/{webhookToken}", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AllHttpMethods_AreStoredCorrectly()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/webhook/{webhookToken}"));
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/webhook/{webhookToken}"));
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/webhook/{webhookToken}"));

        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var methods = list.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("method").GetString())
            .ToHashSet();

        methods.Should().Contain("PUT");
        methods.Should().Contain("DELETE");
        methods.Should().Contain("PATCH");
    }

    [Fact]
    public async Task PostToMalformedGuid_Returns404()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/webhook/not-a-guid", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WebhookService.IntegrationTests;

[Collection("Integration")]
public sealed class TokensApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateToken_Returns201_WithWebhookUrl()
    {
        var response = await _client.PostAsJsonAsync("/api/tokens", new { description = "test token" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("webhookUrl").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetTokens_ReturnsCreatedToken()
    {
        await _client.PostAsJsonAsync("/api/tokens", new { description = "list-test" });

        var response = await _client.GetAsync("/api/tokens");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTokenById_Returns404_ForUnknownId()
    {
        var response = await _client.GetAsync($"/api/tokens/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTokenById_Returns200_ForCreatedToken()
    {
        var created = await _client.PostAsJsonAsync("/api/tokens", new { description = "get-by-id" });
        var token = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = token.GetProperty("id").GetString();

        var response = await _client.GetAsync($"/api/tokens/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteToken_Returns204_ThenGetReturns404()
    {
        var created = await _client.PostAsJsonAsync("/api/tokens", new { description = "to-delete" });
        var token = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = token.GetProperty("id").GetString();

        var deleteResponse = await _client.DeleteAsync($"/api/tokens/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/tokens/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetCustomResponse_Returns204_AndTokenReflectsChange()
    {
        var created = await _client.PostAsJsonAsync("/api/tokens", new { description = "custom-resp" });
        var token = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = token.GetProperty("id").GetString();

        var payload = new
        {
            statusCode = 201,
            contentType = "application/json",
            body = "{\"ok\":true}",
            headers = "{}"
        };
        var putResponse = await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", payload);
        putResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/tokens/{id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        updated.GetProperty("customResponse").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}

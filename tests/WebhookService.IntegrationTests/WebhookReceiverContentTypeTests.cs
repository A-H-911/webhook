using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;

namespace WebhookService.IntegrationTests;

/// <summary>
/// Exercises WebhookController.ReadBodyAsync content-type branches using
/// FormConsumingWebAppFactory, which reproduces the antiforgery filter
/// side-effect of consuming the body stream before the MVC action runs.
/// These tests serve as a regression net for commit ffc4478.
/// </summary>
[Collection("Integration")]
public sealed class WebhookReceiverContentTypeTests(FormConsumingWebAppFactory factory)
    : IClassFixture<FormConsumingWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/tokens", new { description = "content-type-test" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body.GetProperty("id").GetString()!;
        var url = body.GetProperty("webhookUrl").GetString()!;
        return (id, url.Split('/').Last());
    }

    private async Task<JsonElement> GetFirstRequestDetailAsync(string tokenId)
    {
        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var requestId = list.GetProperty("items")[0].GetProperty("id").GetString();
        var detailResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests/{requestId}");
        return await detailResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    [Fact]
    public async Task Receiver_FormEncodedBody_PersistsAsUrlEncodedString()
    {
        // This test would fail without commit ffc4478: the antiforgery filter exhausts
        // the body stream, so Body would be null before the fix.
        var (tokenId, webhookToken) = await CreateTokenAsync();

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "john"),
            new KeyValuePair<string, string>("email", "john@example.com")
        });
        var response = await _client.PostAsync($"/webhook/{webhookToken}", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await GetFirstRequestDetailAsync(tokenId);
        var body = detail.GetProperty("body").GetString();
        body.Should().NotBeNull("form-encoded body must be captured after ffc4478 fix");

        // Parse the stored URL-encoded string and verify round-trip fidelity
        var parsed = HttpUtility.ParseQueryString(body!);
        parsed["username"].Should().Be("john");
        parsed["email"].Should().Be("john@example.com");
    }

    [Fact]
    public async Task Receiver_FormEncodedBody_WithUnicodeSpaces_RoundTrips()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("message", "héllo wörld")
        });
        await _client.PostAsync($"/webhook/{webhookToken}", formContent);

        var detail = await GetFirstRequestDetailAsync(tokenId);
        var body = detail.GetProperty("body").GetString();
        body.Should().NotBeNull();
        HttpUtility.ParseQueryString(body!)["message"].Should().Be("héllo wörld");
    }

    [Fact]
    public async Task Receiver_BinaryOctetStream_PersistsAsBase64()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();

        var binaryBytes = new byte[] { 0xFF, 0xFE, 0xFD };
        var binaryContent = new ByteArrayContent(binaryBytes);
        binaryContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        await _client.PostAsync($"/webhook/{webhookToken}", binaryContent);

        var detail = await GetFirstRequestDetailAsync(tokenId);
        detail.GetProperty("isBodyBase64").GetBoolean().Should().BeTrue();
        detail.GetProperty("body").GetString().Should().Be(Convert.ToBase64String(binaryBytes));
    }

    [Fact]
    public async Task Receiver_JsonBody_StillWorks_WithFormConsumingMiddleware()
    {
        // Regression: the form-consuming middleware must only call ReadFormAsync
        // for form-encoded content types, not interfere with JSON bodies.
        var (tokenId, webhookToken) = await CreateTokenAsync();

        var jsonContent = new StringContent("{\"event\":\"push\"}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/webhook/{webhookToken}", jsonContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await GetFirstRequestDetailAsync(tokenId);
        detail.GetProperty("body").GetString().Should().Be("{\"event\":\"push\"}");
        detail.GetProperty("isBodyBase64").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Receiver_FormEncodedBody_InactiveToken_StillPersists_Returns410()
    {
        // Inactive tokens always persist the request for audit purposes (returns 410 Gone).
        var (tokenId, webhookToken) = await CreateTokenAsync();

        // Deactivate the token
        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("audit", "true")
        });
        var response = await _client.PostAsync($"/webhook/{webhookToken}", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);

        // Request must still be persisted despite 410
        var listResponse = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        list.GetProperty("total").GetInt32().Should().BeGreaterThan(0, "inactive token requests must be persisted");
    }
}

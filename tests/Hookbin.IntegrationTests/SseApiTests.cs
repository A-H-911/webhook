using System.Net.Http.Json;
using System.Net;

namespace Hookbin.IntegrationTests;

[Collection("Integration")]
public sealed class SseApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SseEndpoint_Returns200_WithEventStreamContentType()
    {
        var created = await _client.PostAsJsonAsync("/api/tokens", new { description = "sse-test" });
        var body = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var tokenId = body.GetProperty("id").GetString();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{tokenId}/sse");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task SseEndpoint_Returns404_ForNonExistentToken()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{Guid.NewGuid()}/sse");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
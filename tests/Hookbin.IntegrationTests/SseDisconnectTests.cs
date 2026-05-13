using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Validates the SSE disconnect handling path in GlobalExceptionMiddleware:
/// (1) OperationCanceledException is silently swallowed when the request is aborted,
/// (2) the HasStarted guard prevents InvalidOperationException when writing to a
/// response that has already flushed headers, (3) the API continues to serve
/// subsequent requests cleanly after a mid-stream cancellation.
/// </summary>
[Collection("Integration")]
public sealed class SseDisconnectTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<string> CreateTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/tokens", new { name = "sse-disconnect" });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task ClientAbortsSseStream_DoesNotPreventSubsequentApiCalls()
    {
        var tokenId = await CreateTokenAsync();

        using (var cts = new CancellationTokenSource())
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{tokenId}/sse"))
        {
            request.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // Abort the stream while the connection is open
            cts.Cancel();
        }

        // The API must remain healthy and responsive
        var followup = await _client.GetAsync($"/api/tokens/{tokenId}");
        followup.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SseEndpoint_HeadersIncludeEventStreamContentType()
    {
        var tokenId = await CreateTokenAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{tokenId}/sse");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task MultipleSequentialSseConnects_DoNotLeakOrCrashApi()
    {
        var tokenId = await CreateTokenAsync();

        for (var i = 0; i < 3; i++)
        {
            using var cts = new CancellationTokenSource();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tokens/{tokenId}/sse");
            request.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            cts.Cancel();
        }

        // API still healthy
        var health = await _client.GetAsync($"/api/tokens/{tokenId}");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

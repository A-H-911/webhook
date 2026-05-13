using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.IntegrationTests;

[Collection("Integration")]
public sealed class DashboardMetricsApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/tokens", new { name = "metrics-test" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body.GetProperty("id").GetString()!;
        var url = body.GetProperty("webhookUrl").GetString()!;
        var token = url.Split('/').Last();
        return (id, token);
    }

    private async Task<JsonElement> GetMetricsAsync()
    {
        var response = await _client.GetAsync("/api/dashboard/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    [Fact]
    public async Task DeleteToken_DropsRequestCounts_FromDashboardMetrics()
    {
        // Capture baseline before this test creates anything
        var before = await GetMetricsAsync();
        var allTimeBefore = before.GetProperty("requestsCapturedAllTime").GetInt64();
        var last24hBefore = before.GetProperty("requestsCapturedLast24h").GetInt64();
        var endpointsBefore = before.GetProperty("totalEndpoints").GetInt32();

        var (tokenId, webhookToken) = await CreateTokenAsync();
        var payload = new StringContent("{\"event\":\"test\"}", Encoding.UTF8, "application/json");
        for (var i = 0; i < 3; i++)
            await _client.PostAsync($"/webhook/{webhookToken}", payload);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/tokens/{tokenId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — metrics must return to exactly the values before this test ran
        var after = await GetMetricsAsync();
        after.GetProperty("requestsCapturedAllTime").GetInt64()
            .Should().Be(allTimeBefore, "hard-delete must cascade-delete the 3 captured request rows");
        after.GetProperty("requestsCapturedLast24h").GetInt64()
            .Should().Be(last24hBefore, "hard-delete must cascade-delete the 3 captured request rows");
        after.GetProperty("totalEndpoints").GetInt32()
            .Should().Be(endpointsBefore, "hard-delete must remove the endpoint from the count");
    }

    [Fact]
    public async Task DeleteToken_RemovesWebhookReceiver_Returns404OnSubsequentPost()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        var payload = new StringContent("{}", Encoding.UTF8, "application/json");

        // Confirm it accepts requests while active
        (await _client.PostAsync($"/webhook/{webhookToken}", payload))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        // After hard-delete the receiver returns 404, not 410 (row is gone, not soft-deleted)
        var response = await _client.PostAsync($"/webhook/{webhookToken}", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteToken_RemovesAllCapturedRequestsFromDatabase()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync();
        var payload = new StringContent("{}", Encoding.UTF8, "application/json");
        await _client.PostAsync($"/webhook/{webhookToken}", payload);

        // Confirm request exists before delete
        var reqsBefore = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        reqsBefore.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyBefore = await reqsBefore.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        bodyBefore.GetProperty("total").GetInt32().Should().Be(1);

        // Act
        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        // Token is gone
        var getToken = await _client.GetAsync($"/api/tokens/{tokenId}");
        getToken.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Requests endpoint returns 200 with total:0 — endpoint does not 404 on missing token;
        // the cascade-delete guarantee is that the rowcount drops to zero (no orphans).
        var reqsAfter = await _client.GetAsync($"/api/tokens/{tokenId}/requests");
        reqsAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyAfter = await reqsAfter.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        bodyAfter.GetProperty("total").GetInt32().Should().Be(0, "cascade delete must remove all captured requests");
    }

    [Fact]
    public async Task DeleteToken_DecrementsLiveEndpoints_WhenTokenHadRecentTraffic()
    {
        // Capture baseline before injecting traffic
        var before = await GetMetricsAsync();
        var liveBefore = before.GetProperty("liveEndpoints").GetInt32();

        var (tokenId, webhookToken) = await CreateTokenAsync();
        var payload = new StringContent("{}", Encoding.UTF8, "application/json");
        await _client.PostAsync($"/webhook/{webhookToken}", payload);

        // Confirm our new token is counted as live
        var mid = await GetMetricsAsync();
        mid.GetProperty("liveEndpoints").GetInt32()
            .Should().BeGreaterThanOrEqualTo(liveBefore + 1);

        // Act
        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        // Assert — live count returned to baseline (cascaded request row gone from the 5-min window)
        var after = await GetMetricsAsync();
        after.GetProperty("liveEndpoints").GetInt32()
            .Should().Be(liveBefore,
                "cascade-deleting requests removes the TokenId from the live-endpoints distinct count");
    }
}

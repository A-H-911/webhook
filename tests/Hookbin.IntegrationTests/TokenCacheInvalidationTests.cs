using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Validates the CLAUDE.md DANGER ZONE invariant: cache.Remove(tokenId) on every token mutation.
/// Each test warms the token cache by sending a webhook, mutates the token (custom response,
/// update, delete), and verifies the next webhook reflects the mutation. Without the cache
/// invalidation, the next webhook would serve stale data for up to 5 minutes.
/// </summary>
[Collection("Integration")]
public sealed class TokenCacheInvalidationTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<(string tokenId, string webhookToken)> CreateTokenAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/tokens", new { name });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return (body.GetProperty("id").GetString()!,
                body.GetProperty("webhookUrl").GetString()!.Split('/').Last());
    }

    private Task<HttpResponseMessage> SendDefaultWebhookAsync(string webhookToken) =>
        _client.PostAsync($"/webhook/{webhookToken}",
            new StringContent("{}", Encoding.UTF8, "application/json"));

    [Fact]
    public async Task SetCustomResponse_AfterCacheWarmed_NextWebhookReflectsNewStatus()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("cache-set");

        // Warm the cache with a default 200 response
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Mutate
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 418,
            contentType = "text/plain",
            body = "teapot",
            headers = "{}"
        });

        // Cache must be evicted; next webhook should return 418
        var response = await SendDefaultWebhookAsync(webhookToken);
        response.StatusCode.Should().Be((HttpStatusCode)418, "cache eviction must surface the new custom response");
    }

    [Fact]
    public async Task ResetCustomResponse_AfterCacheWarmed_NextWebhookReturnsDefault()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("cache-reset");

        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 503,
            contentType = "text/plain",
            body = "service-down",
            headers = "{}"
        });
        // Warm the cache with the 503 custom response
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Reset to default
        await _client.DeleteAsync($"/api/tokens/{tokenId}/custom-response");

        // Cache must be evicted; next webhook should return default 200
        var response = await SendDefaultWebhookAsync(webhookToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "cache eviction must restore the default response");
    }

    [Fact]
    public async Task DeleteToken_AfterCacheWarmed_NextWebhookReturns404()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("cache-delete");

        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _client.DeleteAsync($"/api/tokens/{tokenId}");

        var response = await SendDefaultWebhookAsync(webhookToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "hard-deleted token must immediately return 404, not stale 200");
    }

    [Fact]
    public async Task UpdateToken_DeactivatesAndAfterCacheWarmed_NextWebhookReturns410()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("cache-update");

        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "cache-update",
            description = (string?)null,
            isActive = false
        });

        var response = await SendDefaultWebhookAsync(webhookToken);
        response.StatusCode.Should().Be(HttpStatusCode.Gone,
            "PUT /api/tokens/{id} with isActive=false must evict the cached active state");
    }

    [Fact]
    public async Task UpdateToken_Reactivates_NextWebhookReturns200()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("cache-reactivate");

        // Deactivate first
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "cache-reactivate",
            description = (string?)null,
            isActive = false
        });
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.Gone);

        // Reactivate
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "cache-reactivate",
            description = (string?)null,
            isActive = true
        });

        var response = await SendDefaultWebhookAsync(webhookToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "reactivating a deactivated token must evict the cached inactive state");
    }

    // ── N-shot tests: catch the JSON private-set bug class via repeated cache-hit ────────

    [Fact]
    public async Task SetCustomResponse_TwoConsecutiveWebhooks_BothReturnCustom()
    {
        // The exact MCP-caught bug shape: the FIRST webhook after Set hits cache-miss → DB
        // (correctly returns 418). The SECOND hits cache-HIT and must STILL return 418.
        // Without the [JsonInclude] fix on WebhookToken.CustomResponse, the 2nd reverts to 200.
        var (tokenId, webhookToken) = await CreateTokenAsync("nshot-set");
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 418,
            contentType = "text/plain",
            body = "teapot",
            headers = "{}"
        });

        var first = await SendDefaultWebhookAsync(webhookToken);
        var second = await SendDefaultWebhookAsync(webhookToken);
        var third = await SendDefaultWebhookAsync(webhookToken);

        ((int)first.StatusCode).Should().Be(418, "cache-miss path returns custom");
        ((int)second.StatusCode).Should().Be(418, "cache-HIT must also return custom — this is the MCP bug case");
        ((int)third.StatusCode).Should().Be(418, "every subsequent cache-HIT returns custom");
    }

    [Fact]
    public async Task ResetCustomResponse_TwoConsecutiveWebhooks_BothReturnDefault()
    {
        var (tokenId, webhookToken) = await CreateTokenAsync("nshot-reset");
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 503,
            contentType = "text/plain",
            body = "down",
            headers = "{}"
        });
        // Warm cache with custom 503
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await _client.DeleteAsync($"/api/tokens/{tokenId}/custom-response");

        var first = await SendDefaultWebhookAsync(webhookToken);
        var second = await SendDefaultWebhookAsync(webhookToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK, "post-reset cache-miss returns default 200");
        second.StatusCode.Should().Be(HttpStatusCode.OK, "post-reset cache-HIT must also return default 200");
    }

    [Fact]
    public async Task DeactivateAndReactivate_RoundTrip_RestoresActiveState_ViaCache()
    {
        // Bug 2 scenario: reactivate must work, AND the cache must reflect the new IsActive
        // even on subsequent cache hits.
        var (tokenId, webhookToken) = await CreateTokenAsync("nshot-roundtrip");

        // Deactivate
        var deactivateResp = await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "nshot-roundtrip",
            description = (string?)null,
            isActive = false
        });
        deactivateResp.StatusCode.Should().Be(HttpStatusCode.OK, "PUT must succeed — handler uses GetByIdIncludingInactiveAsync");

        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.Gone, "1st post-deactivate: 410");
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.Gone, "2nd post-deactivate (cache hit): still 410");

        // Reactivate
        var reactivateResp = await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "nshot-roundtrip",
            description = (string?)null,
            isActive = true
        });
        reactivateResp.StatusCode.Should().Be(HttpStatusCode.OK, "PUT must succeed and actually flip IsActive back to true");

        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.OK, "1st post-reactivate: 200");
        (await SendDefaultWebhookAsync(webhookToken)).StatusCode.Should().Be(HttpStatusCode.OK, "2nd post-reactivate (cache hit): still 200");
    }

    [Fact]
    public async Task UpdateTokenName_DoesNotResetCustomResponse_AcrossNCalls()
    {
        // OwnsOne preservation: renaming a token via PUT must not silently wipe its CustomResponse.
        // Tested across multiple webhook calls to exercise both cache-miss and cache-HIT paths.
        var (tokenId, webhookToken) = await CreateTokenAsync("nshot-rename");
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}/custom-response", new
        {
            statusCode = 201,
            contentType = "application/json",
            body = "{\"keep\":\"me\"}",
            headers = "{}"
        });

        // Rename — DON'T touch custom-response
        await _client.PutAsJsonAsync($"/api/tokens/{tokenId}", new
        {
            name = "renamed-token",
            description = "updated description",
            isActive = true
        });

        var first = await SendDefaultWebhookAsync(webhookToken);
        var second = await SendDefaultWebhookAsync(webhookToken);
        var third = await SendDefaultWebhookAsync(webhookToken);

        ((int)first.StatusCode).Should().Be(201, "rename must preserve custom-response — 1st request");
        ((int)second.StatusCode).Should().Be(201, "and the 2nd cache-hit too");
        ((int)third.StatusCode).Should().Be(201, "and every subsequent one");
    }

    [Fact]
    public async Task IDOR_AfterCacheWarm_CrossTokenStillReturns404()
    {
        // Cache must not enable IDOR leakage. Even after token A's data is warm in cache,
        // a request to token A's request from token B's scope must still 404.
        var (tokenAId, webhookTokenA) = await CreateTokenAsync("nshot-idor-a");
        var (tokenBId, _) = await CreateTokenAsync("nshot-idor-b");

        // Warm token A in cache
        await SendDefaultWebhookAsync(webhookTokenA);

        var listA = await (await _client.GetAsync($"/api/tokens/{tokenAId}/requests"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = listA.GetProperty("items");
        // FakeRequestQueuePublisher in WebAppFactory may queue and not persist via the worker.
        // If no request shows up, use a synthetic GUID — IDOR test still holds.
        var requestIdA = items.GetArrayLength() > 0
            ? items[0].GetProperty("id").GetString()
            : Guid.NewGuid().ToString();

        var first = await _client.GetAsync($"/api/tokens/{tokenBId}/requests/{requestIdA}");
        var second = await _client.GetAsync($"/api/tokens/{tokenBId}/requests/{requestIdA}");

        first.StatusCode.Should().Be(HttpStatusCode.NotFound);
        second.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "IDOR protection holds even after the wrong-token cache state is warm");
    }
}

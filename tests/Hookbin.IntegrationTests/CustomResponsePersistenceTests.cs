using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Regression net for commit c829383 — WebhookTokenRepository.UpdateAsync switched from
/// EF Core CurrentValues.SetValues (broken for OwnsOne value objects) to explicit
/// SetCustomResponse/ClearCustomResponse mutator calls. These tests prove the owned
/// CustomResponse is persisted, overwritten, cleared, and re-set correctly through the
/// real DB round-trip.
/// </summary>
[Collection("Integration")]
public sealed class CustomResponsePersistenceTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<string> CreateTokenAsync(string name = "custom-resp-persist")
    {
        var resp = await _client.PostAsJsonAsync("/api/tokens", new { name });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task SetCustomResponse_PersistsAllFields()
    {
        var id = await CreateTokenAsync();

        var put = await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201,
            contentType = "application/json",
            body = "{\"ok\":true}",
            headers = "{\"X-Foo\":\"bar\"}"
        });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var token = await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var cr = token.GetProperty("customResponse");

        cr.ValueKind.Should().NotBe(JsonValueKind.Null);
        cr.GetProperty("statusCode").GetInt32().Should().Be(201);
        cr.GetProperty("contentType").GetString().Should().Be("application/json");
        cr.GetProperty("body").GetString().Should().Be("{\"ok\":true}");
        cr.GetProperty("headers").GetString().Should().Be("{\"X-Foo\":\"bar\"}");
    }

    [Fact]
    public async Task SetCustomResponse_TwiceWithDifferentValues_PersistsLatest()
    {
        // This is the exact regression for c829383: with the old CurrentValues.SetValues
        // implementation, the second PUT silently kept the first response's values.
        var id = await CreateTokenAsync();

        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 200,
            contentType = "text/plain",
            body = "first",
            headers = "{}"
        });

        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 418,
            contentType = "application/json",
            body = "{\"second\":true}",
            headers = "{\"X-Replay\":\"v2\"}"
        });

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var cr = (await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("customResponse");

        cr.GetProperty("statusCode").GetInt32().Should().Be(418);
        cr.GetProperty("contentType").GetString().Should().Be("application/json");
        cr.GetProperty("body").GetString().Should().Be("{\"second\":true}");
        cr.GetProperty("headers").GetString().Should().Be("{\"X-Replay\":\"v2\"}");
    }

    [Fact]
    public async Task DeleteCustomResponse_NullsOutAllFields()
    {
        var id = await CreateTokenAsync();
        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 202,
            contentType = "text/plain",
            body = "to-be-removed",
            headers = "{}"
        });

        var deleteResp = await _client.DeleteAsync($"/api/tokens/{id}/custom-response");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var token = await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        token.GetProperty("customResponse").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetClearSet_RetainsLatest()
    {
        var id = await CreateTokenAsync();

        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 200,
            contentType = "text/plain",
            body = "first",
            headers = "{}"
        });
        await _client.DeleteAsync($"/api/tokens/{id}/custom-response");
        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 503,
            contentType = "text/plain",
            body = "after-clear",
            headers = "{}"
        });

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var cr = (await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("customResponse");

        cr.GetProperty("statusCode").GetInt32().Should().Be(503);
        cr.GetProperty("body").GetString().Should().Be("after-clear");
    }

    [Fact]
    public async Task SetCustomResponse_WithNullBody_PersistsNullBody()
    {
        var id = await CreateTokenAsync();

        var put = await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 204,
            contentType = "text/plain",
            body = (string?)null,
            headers = "{}"
        });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var cr = (await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("customResponse");

        cr.GetProperty("statusCode").GetInt32().Should().Be(204);
        cr.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateToken_DoesNotResetCustomResponse()
    {
        // Editing the token's name/description must NOT silently wipe the owned CustomResponse.
        var id = await CreateTokenAsync();
        await _client.PutAsJsonAsync($"/api/tokens/{id}/custom-response", new
        {
            statusCode = 201,
            contentType = "application/json",
            body = "{\"keep\":\"me\"}",
            headers = "{}"
        });

        await _client.PutAsJsonAsync($"/api/tokens/{id}",
            new { name = "renamed-token", description = "updated", isActive = true });

        var get = await _client.GetAsync($"/api/tokens/{id}");
        var token = await get.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var cr = token.GetProperty("customResponse");

        cr.ValueKind.Should().NotBe(JsonValueKind.Null, "renaming a token must preserve its custom response");
        cr.GetProperty("statusCode").GetInt32().Should().Be(201);
        cr.GetProperty("body").GetString().Should().Be("{\"keep\":\"me\"}");
    }
}

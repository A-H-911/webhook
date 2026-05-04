namespace WebhookService.IntegrationTests;

[Collection("Integration")]
public sealed class HealthCheckTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LiveEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyEndpoint_ReturnsHealthy_WhenDatabaseUp()
    {
        var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}

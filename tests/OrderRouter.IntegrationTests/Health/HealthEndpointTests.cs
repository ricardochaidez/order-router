using System.Net;
using System.Text.Json;
using OrderRouter.IntegrationTests.Persistence;

namespace OrderRouter.IntegrationTests.Health;

public class HealthEndpointTests(ReferenceDataFixture fixture)
{
    [Fact]
    public async Task GetHealth_DatabaseReachableNoCredentials_Returns200HealthyAndNoStore()
    {
        await using var factory = ApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task GetHealth_EmptyUnmigratedDatabase_Returns200Healthy()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var factory = ApiFactory.Create(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetHealth_DatabaseGoneAfterStartup_Returns503UnhealthyWithoutDetails()
    {
        var database = await ThrowawayDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var factory = ApiFactory.Create(database.ConnectionString);
        using var client = factory.CreateClient();

        using var before = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Dropping the database stands in for PostgreSQL going away while the API keeps running (AC4).
        await database.DisposeAsync();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task GetOpenApiDocument_Always_DescribesHealthWith200And503()
    {
        await using var factory = ApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var responses = document.RootElement.GetProperty("paths").GetProperty("/health").GetProperty("get").GetProperty("responses");

        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("503", out _));
    }
}

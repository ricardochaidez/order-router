using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using OrderRouter.Api.Authentication;
using OrderRouter.IntegrationTests.Health;

namespace OrderRouter.IntegrationTests.Authentication;

public class AuthenticationTests(ReferenceDataFixture fixture)
{
    private const string devKey = "dev-only-test-key";

    private static HttpRequestMessage Probe(string? bearer = null, string? apiKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AuthApiFactory.ProbePath);

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (apiKey is not null)
        {
            request.Headers.Add(DevApiKeyDefaults.HeaderName, apiKey);
        }

        return request;
    }

    [Fact]
    public async Task Probe_NoCredentials_Returns401WithChallengeAndHandlerDoesNotRun()
    {
        var probe = new AuthApiFactory.ProbeCounter();
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, probe: probe);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
        Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("badSignature")]
    [InlineData("wrongAudience")]
    [InlineData("wrongIssuer")]
    [InlineData("garbage")]
    public async Task Probe_InvalidToken_Returns401(string kind)
    {
        var token = kind switch
        {
            "expired" => TestJwt.Expired(),
            "badSignature" => TestJwt.BadSignature(),
            "wrongAudience" => TestJwt.WrongAudience(),
            "wrongIssuer" => TestJwt.WrongIssuer(),
            _ => "not-a-jwt",
        };
        var probe = new AuthApiFactory.ProbeCounter();
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, probe: probe);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(bearer: token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Probe_ValidToken_ReturnsOkWithSubject(string environment)
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, environment);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(bearer: TestJwt.Valid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(TestJwt.Subject, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_NoCredentials_IsNotUnauthorized()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopmentNoCredentials_Returns200()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPath_NoCredentials_Returns401FromFallbackPolicy()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        // The fallback policy covers unmatched paths too, so nothing reveals which paths exist without credentials.
        using var response = await client.GetAsync("/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Probe_DevKeyCorrectInDevelopment_ReturnsOkAsDevKeyCaller()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, devApiKey: devKey);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(apiKey: devKey), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(DevApiKeyDefaults.CallerId, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    public async Task Probe_DevKeyWrongInDevelopment_Returns401(string sent)
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, devApiKey: devKey);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(apiKey: sent), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Probe_DevKeyConfiguredInProduction_Returns401()
    {
        var probe = new AuthApiFactory.ProbeCounter();
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, "Production", devKey, probe);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(apiKey: devKey), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Probe_DevKeyNotConfiguredInDevelopment_KeyIsIgnored()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Probe(apiKey: devKey), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Startup_ProductionWithoutJwtSettings_FailsNamingTheSetting()
    {
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:OrderRouter", fixture.ConnectionString);
            builder.UseSetting("Authentication:Jwt:Authority", "");
            builder.UseSetting("CallerHashing:Secret", TestCallerHashing.Secret);
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Authentication:Jwt:Authority", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopmentWithDevKey_ListsBearerAndApiKeySchemes()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString, devApiKey: devKey);
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        Assert.Equal("bearer", schemes.GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.Equal(DevApiKeyDefaults.HeaderName, schemes.GetProperty("ApiKey").GetProperty("name").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopmentWithoutDevKey_ListsOnlyBearer()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        Assert.True(schemes.TryGetProperty("Bearer", out _));
        Assert.False(schemes.TryGetProperty("ApiKey", out _));
    }
}

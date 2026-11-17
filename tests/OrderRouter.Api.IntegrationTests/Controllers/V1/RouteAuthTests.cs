using System.Net;
using System.Text.Json;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>The OR-008 follow-up: authentication runs before binding and validation on both paths (FR-19).</summary>
public class RouteAuthTests(ReferenceDataFixture fixture)
{
    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_NoCredentialsAndInvalidBody_Returns401WithChallengeAndNoEnvelope(string path)
    {
        var useCase = new RecordingRouteUseCase();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, "{ not json", authenticated: false), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
        Assert.DoesNotContain("feasible", body, StringComparison.Ordinal);
        Assert.Empty(useCase.Commands);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_NoCredentialsAndValidBody_Returns401AndRoutingDoesNotRun(string path)
    {
        var useCase = new RecordingRouteUseCase();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, RouteApiFactory.ValidBody, authenticated: false), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(useCase.Commands);
    }

    // An unexpected fault must fail safe: a 500 with no internals, never a body that claims the order was routed.
    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_UseCaseThrows_Returns500ProblemDetailsWithoutInternals(string path)
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, new ThrowingRouteUseCase());
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, RouteApiFactory.ValidBody), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("feasible", out _));
        Assert.DoesNotContain("NotSupportedException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret internal detail", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
    }
}

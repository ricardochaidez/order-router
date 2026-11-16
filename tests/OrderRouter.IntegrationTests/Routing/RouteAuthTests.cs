using System.Net;
using System.Text.Json;

namespace OrderRouter.IntegrationTests.Routing;

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

    // The placeholder (until OR-015) must fail safe: a valid order is never reported as routed.
    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_ValidOrderWithPlaceholderUseCase_Returns500ProblemDetailsWithoutInternals(string path)
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, RouteApiFactory.ValidBody), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("feasible", out _));
        Assert.DoesNotContain("NotSupportedException", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OR-015", text, StringComparison.Ordinal);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.Constants;
using OrderRouter.Api.Filters;
using OrderRouter.Domain.Entities.Routing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>AC8: <c>POST /api/route</c> answers exactly like <c>POST /api/v1/route</c> (ADR-0004).</summary>
public class RouteAliasTests(ReferenceDataFixture fixture)
{
    public static TheoryData<string> Requests =>
    [
        RouteApiFactory.ValidBody,
        """{ "order_id": "A", "items": [] }""",
        """{ "order_id": "A", "customer_zip": "2130", "items": [ { "product_code": "A", "quantity": 0 } ] }""",
        "{ not json",
        "",
        """{ "order_id": "A", "customer_zip": "10015", "items": [ { "product_code": "XX-UNKNOWN-999", "quantity": 1 } ] }""",
    ];

    private static readonly StoredRoutingOrder routed = new(
        Guid.CreateVersion7(), "ORD-EXAMPLE", null, "10015", false, "routed", true,
        """{ "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" }, "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" }, "exp3_partial_routing": { "arm": "control", "reason": "disabled" } }""",
        "defaults", null, new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
        [new StoredOrderItem(1, "WC-STD-001", 1, "wheelchair", "SUP-1", "Care", "local", null, null)], []);

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task Post_SameBody_AliasMatchesV1StatusAndBody(string body)
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, new RecordingRouteUseCase(routed));
        using var client = factory.CreateClient();

        using var v1 = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", body), TestContext.Current.CancellationToken);
        using var alias = await client.SendAsync(RouteApiFactory.Post("/api/route", body), TestContext.Current.CancellationToken);

        Assert.Equal(v1.StatusCode, alias.StatusCode);
        Assert.Equal(
            await v1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await alias.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Post_NoCredentials_AliasAndV1BothReturn401()
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, new RecordingRouteUseCase());
        using var client = factory.CreateClient();

        using var v1 = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", RouteApiFactory.ValidBody, authenticated: false), TestContext.Current.CancellationToken);
        using var alias = await client.SendAsync(RouteApiFactory.Post("/api/route", RouteApiFactory.ValidBody, authenticated: false), TestContext.Current.CancellationToken);

        Assert.Equal(v1.StatusCode, alias.StatusCode);
        Assert.Equal(v1.Headers.WwwAuthenticate.ToString(), alias.Headers.WwwAuthenticate.ToString());
    }

    // Guards against drift: the shared configuration must leave both endpoints with the same policy and contract.
    [Fact]
    public async Task Endpoints_V1AndAlias_CarryTheSameMetadata()
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, new RecordingRouteUseCase());
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        // Attribute-route templates have no leading slash; trimming keeps the check independent of how the route is declared.
        var v1 = endpoints.Single(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == "api/v1/route");
        var alias = endpoints.Single(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == "api/route");

        Assert.Equal(["POST"], v1.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(["POST"], alias.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(v1.Metadata.GetMetadata<RouteEnvelopeAttribute>());
        Assert.NotNull(alias.Metadata.GetMetadata<RouteEnvelopeAttribute>());
        Assert.Equal(PolicyNames(v1), PolicyNames(alias));
        Assert.Contains(AuthorizationPolicyNames.RouteCaller, PolicyNames(v1));

        // Controllers declare their responses with [ProducesResponseType], which MVC exposes through ApiExplorer.
        var descriptions = factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items.SelectMany(group => group.Items).ToArray();
        Assert.Equal(ProducedStatusCodes(descriptions, "api/v1/route"), ProducedStatusCodes(descriptions, "api/route"));
        Assert.Equal([201, 400, 500], ProducedStatusCodes(descriptions, "api/v1/route"));
    }

    private static string[] PolicyNames(Endpoint endpoint)
        => [.. endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy ?? string.Empty).Order()];

    private static int[] ProducedStatusCodes(IEnumerable<ApiDescription> descriptions, string path)
        => [.. descriptions.Single(description => description.HttpMethod == "POST" && description.RelativePath == path).SupportedResponseTypes.Select(response => response.StatusCode).Order()];
}

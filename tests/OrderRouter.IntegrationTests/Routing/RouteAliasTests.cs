using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.Authentication;
using OrderRouter.Api.Routing;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.IntegrationTests.Routing;

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

    private static readonly RouteResponse routed = new()
    {
        OrderId = "ORD-EXAMPLE",
        Status = RoutingStatus.Routed,
        Routing = [new SupplierRouting("SUP-1", "Care", [new RoutedItem("WC-STD-001", 1, "wheelchair", "local")])],
    };

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

        var v1 = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/v1/route");
        var alias = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/route");

        Assert.Equal(["POST"], v1.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(["POST"], alias.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(v1.Metadata.GetMetadata<RouteEnvelopeMetadata>());
        Assert.NotNull(alias.Metadata.GetMetadata<RouteEnvelopeMetadata>());
        Assert.Equal(PolicyNames(v1), PolicyNames(alias));
        Assert.Contains(AuthorizationPolicyNames.RouteCaller, PolicyNames(v1));
        Assert.Equal(ProducedStatusCodes(v1), ProducedStatusCodes(alias));
        Assert.Equal([201, 400, 500], ProducedStatusCodes(v1));
    }

    private static string[] PolicyNames(Endpoint endpoint)
        => [.. endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy ?? string.Empty).Order()];

    private static int[] ProducedStatusCodes(Endpoint endpoint)
        => [.. endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(metadata => metadata.StatusCode).Order()];
}

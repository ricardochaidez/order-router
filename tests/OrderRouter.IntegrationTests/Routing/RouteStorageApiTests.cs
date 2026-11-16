using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderRouter.Application.Interfaces;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Storage;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Persistence.RoutingOrders;
using OrderRouter.IntegrationTests.Authentication;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>
/// OR-027 acceptance: a valid POST stores the decision in the seeded database and answers 201 with the stored id
/// and a <c>Location</c> (FR-20, SC-28..SC-30). Every test uses its own <c>order_id</c>, because the shared database
/// accumulates stored orders (expected, NFR-08).
/// </summary>
public class RouteStorageApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    private static string Body(string orderId, params string[] codes)
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "10015", "mail_order": false,
              "items": [ {{string.Join(", ", codes.Select(code => $$"""{ "product_code": "{{code}}", "quantity": 1 }"""))}} ] }
            """;

    private static string Routable(string orderId) => Body(orderId, "WC-STD-001", "OX-PORT-024");

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> PostAsync(
        WebApplicationFactory<Program> factory, string path, string body, string? token = null)
    {
        using var client = factory.CreateClient();
        var response = await client.SendAsync(RouteApiFactory.Post(path, body, token: token), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        var json = text.Length > 0 && text[0] == '{' ? JsonDocument.Parse(text).RootElement.Clone() : default;

        return (response, json);
    }

    private Task<List<string>> QueryAsync(string sql) => fixture.QueryAsync(sql, Token);

    private async Task<string> CountOrdersAsync(string orderId)
        => Assert.Single(await QueryAsync($"SELECT count(*) FROM routing_orders WHERE order_id = '{orderId}'"));

    // AC1
    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_RoutedOrder_Returns201WithIdAndLocationAndStoresOrderAndItems(string path)
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        var (response, json) = await PostAsync(factory, path, Routable(orderId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = Guid.Parse(json.GetProperty("id").GetString()!);
        Assert.Equal(7, id.Version);
        Assert.Equal(orderId, json.GetProperty("order_id").GetString());
        Assert.Equal($"/api/v1/route/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal([$"{id}|routed"], await QueryAsync($"SELECT id || '|' || status FROM routing_orders WHERE order_id = '{orderId}'"));
        Assert.Equal(
            ["1|WC-STD-001|SUP-0810|local", "2|OX-PORT-024|SUP-0810|local"],
            await QueryAsync($"SELECT line_number || '|' || product_code || '|' || supplier_id || '|' || fulfillment_mode FROM routing_order_items WHERE routing_order_id = '{id}' ORDER BY line_number"));
        Assert.All(
            await QueryAsync($"SELECT supplier_name FROM routing_order_items WHERE routing_order_id = '{id}'"),
            name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    // AC2
    [Fact]
    public async Task Post_NotRoutedOrder_Returns201AndStoresTheUnknownLineWithItsReason()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        var (response, json) = await PostAsync(factory, "/api/v1/route", Body(orderId, "WC-STD-001", "XX-UNKNOWN-999"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("not_routed", json.GetProperty("status").GetString());
        var id = json.GetProperty("id").GetString();
        Assert.Equal(
            ["XX-UNKNOWN-999|UNKNOWN_PRODUCT|Product code 'XX-UNKNOWN-999' was not found in the catalog."],
            await QueryAsync($"SELECT product_code || '|' || reason_code || '|' || message FROM routing_order_items WHERE routing_order_id = '{id}' AND reason_code IS NOT NULL"));
        Assert.Equal(["2"], await QueryAsync($"SELECT count(*) FROM routing_order_items WHERE routing_order_id = '{id}'"));
    }

    // AC3
    [Fact]
    public async Task Post_AnyOrder_StoresArmsConfigVersionSearchModeAndCreatedAt()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        await PostAsync(factory, "/api/v1/route", Routable(orderId));

        Assert.Equal(
            ["control|disabled|exact"],
            await QueryAsync(
                "SELECT (experiment_arms -> 'exp3_partial_routing' ->> 'arm') || '|' || (experiment_arms -> 'exp3_partial_routing' ->> 'reason') || '|' || search_mode " +
                $"FROM routing_orders WHERE order_id = '{orderId}'"));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(await QueryAsync($"SELECT config_version FROM routing_orders WHERE order_id = '{orderId}'"))));
        Assert.Equal(["true"], await QueryAsync($"SELECT (created_at > '{before:O}'::timestamptz AND created_at <= now())::text FROM routing_orders WHERE order_id = '{orderId}'"));
    }

    // AC4
    [Fact]
    public async Task Post_SameBodyTwice_ReturnsTwoDifferentIdsAndStoresTwoOrders()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        var (_, first) = await PostAsync(factory, "/api/v1/route", Routable(orderId));
        var (_, second) = await PostAsync(factory, "/api/v1/route", Routable(orderId));

        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal("2", await CountOrdersAsync(orderId));
    }

    // AC6
    [Fact]
    public async Task Post_InvalidRequest_Returns400AndStoresNothing()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);
        var (response, json) = await PostAsync(factory, "/api/v1/route", $$"""{ "order_id": "{{orderId}}", "customer_zip": "123", "items": [] }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(json.TryGetProperty("id", out _));
        Assert.Equal(["0"], await QueryAsync($"SELECT count(*) FROM routing_orders WHERE order_id = '{orderId}'"));
    }

    // AC6
    [Fact]
    public async Task Post_NoCredentials_Returns401AndStoresNothing()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", Routable(orderId), authenticated: false), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("0", await CountOrdersAsync(orderId));
    }

    // AC7: the database rejects the insert for real (a check constraint), so the rollback is the real one.
    [Fact]
    public async Task Post_DatabaseRejectsTheInsert_Returns500ProblemDetailsWithNoIdAndNoRows()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRoutingOrderStore>();
                services.AddScoped<IRoutingOrderStore>(provider => new RejectingStore(new EfRoutingOrderStore(provider.GetRequiredService<OrderRouterDbContext>())));
            }));

        var (response, json) = await PostAsync(factory, "/api/v1/route", Routable(orderId));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(json.TryGetProperty("id", out _));
        Assert.False(json.TryGetProperty("routing", out _));
        Assert.Null(response.Headers.Location);
        Assert.Equal("0", await CountOrdersAsync(orderId));
        Assert.Equal(["0"], await QueryAsync($"SELECT count(*) FROM routing_order_items i JOIN routing_orders o ON o.id = i.routing_order_id WHERE o.order_id = '{orderId}'"));
    }

    // AC8
    [Fact]
    public async Task Post_CallerUser123_StoresOnlyItsHash()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        await PostAsync(factory, "/api/v1/route", Routable(orderId), token: TestJwt.ForSubject("user-123"));

        var hash = Assert.Single(await QueryAsync($"SELECT caller_hash FROM routing_orders WHERE order_id = '{orderId}'"));
        Assert.Equal(factory.Services.GetRequiredService<ICallerHasher>().Hash("user-123"), hash);
        Assert.DoesNotContain("user-123", hash, StringComparison.Ordinal);
        Assert.Equal(["0"], await QueryAsync($"SELECT count(*) FROM routing_orders o WHERE o::text LIKE '%user-123%' AND order_id = '{orderId}'"));
        Assert.Equal(
            ["0"],
            await QueryAsync($"SELECT count(*) FROM routing_order_items i JOIN routing_orders o ON o.id = i.routing_order_id WHERE o.order_id = '{orderId}' AND i::text LIKE '%user-123%'"));
    }

    // Decided with the engineer: no caller claim means a NULL hash and the order can never be read back.
    [Fact]
    public async Task Post_TokenWithNoCallerClaim_Returns201AndStoresANullHash()
    {
        var orderId = NewOrderId();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        var (response, _) = await PostAsync(factory, "/api/v1/route", Routable(orderId), token: TestJwt.WithoutCaller());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["NULL"], await QueryAsync($"SELECT coalesce(caller_hash, 'NULL') FROM routing_orders WHERE order_id = '{orderId}'"));
    }

    // Makes the database reject the insert by sending a quantity that violates ck_routing_order_items_quantity.
    private sealed class RejectingStore(IRoutingOrderStore inner) : IRoutingOrderStore
    {
        public Task SaveAsync(StoredRoutingOrder order, CancellationToken cancellationToken)
            => inner.SaveAsync(order with { Items = [.. order.Items.Select((item, index) => index == 1 ? item with { Quantity = 0 } : item)] }, cancellationToken);
    }
}

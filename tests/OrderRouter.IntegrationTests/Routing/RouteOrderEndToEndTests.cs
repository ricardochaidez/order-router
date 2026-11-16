using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderRouter.IntegrationTests.Persistence;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>
/// OR-015 acceptance: the real host, real use case and the seeded reference data, on both paths (ADR-0004).
/// Each test asserts 200 and <c>feasible == (status == "routed")</c>; OR-027 changes 200 to 201.
/// </summary>
public class RouteOrderEndToEndTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string ord001 = """
        { "order_id": "ORD-001", "customer_zip": "10015", "mail_order": false,
          "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "OX-PORT-024", "quantity": 1 } ] }
        """;

    private const string ord002 = """
        { "order_id": "ORD-002", "customer_zip": "77059", "mail_order": false,
          "items": [ { "product_code": "HB-FUL-018", "quantity": 1 }, { "product_code": "PL-ELEC-043", "quantity": 1 },
                     { "product_code": "CM-BED-048", "quantity": 1 }, { "product_code": "BP-AUTO-077", "quantity": 1 } ] }
        """;

    private const string ord003 = """
        { "order_id": "ORD-003", "customer_zip": "02130", "mail_order": true,
          "items": [ { "product_code": "CP-STD-031", "quantity": 1 }, { "product_code": "CP-MSK-FF-035", "quantity": 2 },
                     { "product_code": "NB-COMP-039", "quantity": 1 } ] }
        """;

    private async Task<JsonElement> PostAsync(string connectionString, string path, string body)
    {
        await using var factory = RouteApiFactory.Create(connectionString, useCase: null);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, body), Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but got {(int)response.StatusCode}: {text}");
        var json = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(json.GetProperty("status").GetString() == "routed", json.GetProperty("feasible").GetBoolean());

        return json;
    }

    private Task<JsonElement> PostAsync(string path, string body) => PostAsync(fixture.ConnectionString, path, body);

    private static string[] SupplierIds(JsonElement body) => [.. body.GetProperty("routing").EnumerateArray().Select(g => g.GetProperty("supplier_id").GetString()!)];

    private static IEnumerable<JsonElement> Items(JsonElement body) => body.GetProperty("routing").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray());

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord001_RoutesToOneLocalSupplier(string path)
    {
        // AC1
        var body = await PostAsync(path, ord001);

        Assert.Equal("routed", body.GetProperty("status").GetString());
        Assert.Equal("ORD-001", body.GetProperty("order_id").GetString());
        Assert.Equal(["SUP-0810"], SupplierIds(body));
        Assert.Equal(2, Items(body).Count());
        Assert.All(Items(body), item => Assert.Equal("local", item.GetProperty("fulfillment_mode").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord002_RoutesToTwoLocalSuppliers(string path)
    {
        // AC2
        var body = await PostAsync(path, ord002);

        Assert.Equal("routed", body.GetProperty("status").GetString());
        Assert.Equal(["SUP-0375", "SUP-042"], SupplierIds(body).Order(StringComparer.Ordinal));
        var bySupplier = body.GetProperty("routing").EnumerateArray().ToDictionary(
            g => g.GetProperty("supplier_id").GetString()!,
            g => g.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("product_code").GetString()!).ToArray());
        Assert.Equal(["HB-FUL-018", "PL-ELEC-043", "CM-BED-048"], bySupplier["SUP-0375"]);
        Assert.Equal(["BP-AUTO-077"], bySupplier["SUP-042"]);
        Assert.All(Items(body), item => Assert.Equal("local", item.GetProperty("fulfillment_mode").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord003_RoutesOneMailShipmentFromAnEligibleSupplier(string path)
    {
        // AC3: the two suppliers tie fully, so either may win (key 5 is random)
        var body = await PostAsync(path, ord003);

        Assert.Equal("routed", body.GetProperty("status").GetString());
        var supplier = Assert.Single(SupplierIds(body));
        Assert.Contains(supplier, new[] { "SUP-023", "SUP-065" });
        Assert.Equal(3, Items(body).Count());
        Assert.All(Items(body), item => Assert.Equal("mail", item.GetProperty("fulfillment_mode").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_RoutedItems_HaveAllFieldsInRequestOrder(string path)
    {
        // AC4
        var body = await PostAsync(path, ord001);

        var items = Items(body).ToArray();
        Assert.Equal(["WC-STD-001", "OX-PORT-024"], items.Select(i => i.GetProperty("product_code").GetString()));
        Assert.All(items, item =>
        {
            Assert.Equal(1, item.GetProperty("quantity").GetInt32());
            Assert.False(string.IsNullOrEmpty(item.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrEmpty(item.GetProperty("fulfillment_mode").GetString()));
        });
        Assert.Empty(body.GetProperty("warnings").EnumerateArray());
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
        Assert.False(body.TryGetProperty("id", out _));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_OneUnknownProduct_IsNotRoutedUnderControl(string path)
    {
        // AC5
        var body = await PostAsync(path, """
            { "order_id": "ORD-X", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "XX-UNKNOWN-999", "quantity": 3 } ] }
            """);

        Assert.Equal("not_routed", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("routing").EnumerateArray());
        var item = Assert.Single(body.GetProperty("unfulfilled_items").EnumerateArray());
        Assert.Equal("XX-UNKNOWN-999", item.GetProperty("product_code").GetString());
        Assert.Equal(3, item.GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("category").ValueKind);
        Assert.Equal("UNKNOWN_PRODUCT", item.GetProperty("reason_code").GetString());
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal(item.GetProperty("message").GetString(), error.GetString());
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_NoSupplierInArea_IsNotRoutedWithTheAreaReason(string path)
    {
        // §9.4: ZIP 00050 is outside every wheelchair supplier area in the seed data, mail off
        var body = await PostAsync(path, """
            { "order_id": "ORD-Y", "customer_zip": "00050", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 } ] }
            """);

        Assert.Equal("not_routed", body.GetProperty("status").GetString());
        var item = Assert.Single(body.GetProperty("unfulfilled_items").EnumerateArray());
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY_IN_AREA", item.GetProperty("reason_code").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("category").ValueKind);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_EmptyMigratedDatabase_ReturnsNotFeasibleWithEveryItemUnknown(string path)
    {
        // AC6
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync(Token);
        }

        var body = await PostAsync(database.ConnectionString, path, ord001);

        Assert.False(body.GetProperty("feasible").GetBoolean());
        Assert.Empty(body.GetProperty("routing").EnumerateArray());
        Assert.Equal(2, body.GetProperty("unfulfilled_items").GetArrayLength());
        Assert.All(
            body.GetProperty("unfulfilled_items").EnumerateArray(),
            item => Assert.Equal("UNKNOWN_PRODUCT", item.GetProperty("reason_code").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_SingleLineOrder_IsRouted(string path)
    {
        // AC8
        var body = await PostAsync(path, """
            { "order_id": "ORD-1L", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 2 } ] }
            """);

        Assert.Equal("routed", body.GetProperty("status").GetString());
        var item = Assert.Single(Items(body));
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_DuplicateProductCode_RoutesBothLinesFromTheSameSupplier(string path)
    {
        // AC8, DEV-10
        var body = await PostAsync(path, """
            { "order_id": "ORD-DUP", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "WC-STD-001", "quantity": 2 } ] }
            """);

        Assert.Equal("routed", body.GetProperty("status").GetString());
        var group = Assert.Single(body.GetProperty("routing").EnumerateArray());
        Assert.Equal([1, 2], group.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("quantity").GetInt32()));
    }

    [Fact]
    public async Task Post_Routing_DoesNotChangeReferenceData()
    {
        // OR-006 follow-up: routing only reads. Row counts and the row versions (xmin) stay the same.
        var queries = new[] { "suppliers", "products", "supplier_categories", "supplier_service_areas" }
            .Select(table => $"SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM {table}")
            .ToArray();

        async Task<string[]> SnapshotAsync()
            => [.. await Task.WhenAll(queries.Select(async sql => Assert.Single(await fixture.QueryAsync(sql, Token))))];

        var before = await SnapshotAsync();

        await PostAsync("/api/v1/route", ord001);
        await PostAsync("/api/v1/route", ord002);
        await PostAsync("/api/v1/route", ord003);

        Assert.Equal(before, await SnapshotAsync());
    }
}

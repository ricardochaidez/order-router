using System.Text.Json;
using OrderRouter.IntegrationTests.Routing;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-023 AC1: the three sample orders (requirements §6.2) through the real host on both paths. The bodies are the
/// ones in <c>Docs/Data/sample_orders.json</c> with a unique <c>order_id</c> per call. Experiments ship disabled, so
/// every request is in control.
/// </summary>
public class SampleOrderAcceptanceTests(ReferenceDataFixture fixture)
{
    private readonly AcceptanceApi api = new(fixture);

    internal static string Ord001(string orderId) => AcceptanceApi.Body(orderId, "10015", false, ("WC-STD-001", 1), ("OX-PORT-024", 1));

    internal static string Ord002(string orderId) => AcceptanceApi.Body(orderId, "77059", false, ("HB-FUL-018", 1), ("PL-ELEC-043", 1), ("CM-BED-048", 1), ("BP-AUTO-077", 1));

    internal static string Ord003(string orderId) => AcceptanceApi.Body(orderId, "02130", true, ("CP-STD-031", 1), ("CP-MSK-FF-035", 2), ("NB-COMP-039", 1));

    private static string[] Codes(JsonElement group) => [.. group.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("product_code").GetString()!)];

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord001_RoutesToOneLocalSupplier(string path)
    {
        // AC1
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, Ord001(AcceptanceApi.NewOrderId()));

        Assert.True(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal("routed", reply.Json.GetProperty("status").GetString());
        Assert.Equal(Or023Expectations.Ord001, reply.SupplierIds);
        Assert.Equal(2, reply.Items.Length);
        Assert.All(reply.Items, item => Assert.Equal("local", item.GetProperty("fulfillment_mode").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord002_RoutesToTwoLocalSuppliers(string path)
    {
        // AC1
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, Ord002(AcceptanceApi.NewOrderId()));

        Assert.True(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal("routed", reply.Json.GetProperty("status").GetString());
        Assert.Equal(Or023Expectations.Ord002, reply.SupplierIds.Order(StringComparer.Ordinal));
        var groups = reply.Json.GetProperty("routing").EnumerateArray().ToDictionary(group => group.GetProperty("supplier_id").GetString()!);
        Assert.Equal(["HB-FUL-018", "PL-ELEC-043", "CM-BED-048"], Codes(groups["SUP-0375"]));
        Assert.Equal(["BP-AUTO-077"], Codes(groups["SUP-042"]));
        Assert.All(reply.Items, item => Assert.Equal("local", item.GetProperty("fulfillment_mode").GetString()));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_Ord003_RoutesOneMailShipmentFromOneOfTheTiedSuppliers(string path)
    {
        // AC1: SUP-023 and SUP-065 tie fully, so either may win.
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, Ord003(AcceptanceApi.NewOrderId()));

        Assert.True(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal("routed", reply.Json.GetProperty("status").GetString());
        Assert.Contains(Assert.Single(reply.SupplierIds), Or023Expectations.Ord003Tied);
        Assert.Equal(3, reply.Items.Length);
        Assert.All(reply.Items, item => Assert.Equal("mail", item.GetProperty("fulfillment_mode").GetString()));
    }
}

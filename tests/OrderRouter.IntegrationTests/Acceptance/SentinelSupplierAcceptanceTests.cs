using System.Net;
using OrderRouter.IntegrationTests.Routing;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-023 AC2 to AC7: the sentinel suppliers (<c>SUP-T001</c> to <c>SUP-T014</c> in the seed data) through the real
/// host on both paths. Product codes come from <c>Docs/Data/Seed/products.normalized.csv</c>:
/// wheelchair <c>WC-STD-001</c>, walker <c>WK-STD-009</c>, cane <c>CN-STD-063</c>, rollator <c>RL-STD-058</c>.
/// Random ties assert membership of the tied pair, never a fixed winner (NFR-08).
/// </summary>
public class SentinelSupplierAcceptanceTests(ReferenceDataFixture fixture)
{
    private readonly AcceptanceApi api = new(fixture);

    private const string wheelchair = "WC-STD-001";
    private const string walker = "WK-STD-009";
    private const string cane = "CN-STD-063";
    private const string rollator = "RL-STD-058";

    // Enough calls to catch an outsider in a tied pair; never used to assert that both members appear.
    private const int tieRepeats = 8;

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_LocalExclusiveWheelchair_RoutesToSentinelT001(string path)
    {
        // AC2
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00001", false, (wheelchair, 1)));

        Assert.Equal(Or023Expectations.ExclusiveWheelchair, reply.SupplierIds);
        Assert.Equal("local", Assert.Single(reply.Items).GetProperty("fulfillment_mode").GetString());
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_LocalPreferenceWalker_RoutesToSentinelT005(string path)
    {
        // AC3
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00005", false, (walker, 1)));

        Assert.Equal(Or023Expectations.LocalPreferenceWalker, reply.SupplierIds);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_MailOrderWalker_ExcludesLocalOnlySentinelT005AndShipsByMail(string path)
    {
        // AC4: SC-06, a supplier that cannot mail order is not eligible when mail_order is true.
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00005", true, (walker, 1)));

        Assert.True(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.DoesNotContain("SUP-T005", reply.SupplierIds);
        Assert.Equal("mail", Assert.Single(reply.Items).GetProperty("fulfillment_mode").GetString());
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_WheelchairCaneWalker_RoutesToGeneralistT007AsOneShipment(string path)
    {
        // AC5
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00007", false, (wheelchair, 1), (cane, 1), (walker, 1)));

        Assert.Equal(Or023Expectations.Generalist, reply.SupplierIds);
        Assert.Equal(3, reply.Items.Length);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_TiedCaneSuppliers_AlwaysRoutesToOneOfThePair(string path)
    {
        // AC6: SUP-T003 and SUP-T004 tie on every key.
        await using var factory = api.CreateFactory();

        for (var i = 0; i < tieRepeats; i++)
        {
            var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00003", false, (cane, 1)));

            Assert.Contains(Assert.Single(reply.SupplierIds), Or023Expectations.TiedCane);
        }
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_TiedUnratedRollatorSuppliers_AreRoutableAndAlwaysOneOfThePair(string path)
    {
        // AC6: SUP-T011 and SUP-T012 have no rating, which must not make them unroutable.
        await using var factory = api.CreateFactory();

        for (var i = 0; i < tieRepeats; i++)
        {
            var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00040", false, (rollator, 1)));

            Assert.True(reply.Json.GetProperty("feasible").GetBoolean());
            Assert.Contains(Assert.Single(reply.SupplierIds), Or023Expectations.TiedUnratedRollator);
        }
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_NoSupplierForTheCategoryInTheArea_IsABusinessOutcomeNotAnError(string path)
    {
        // AC7: 00098 is served only by the wheelchair sentinel SUP-T014, so a cane has no supplier.
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostCreatedAsync(factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00098", false, (cane, 1)));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.False(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.Empty(reply.Json.GetProperty("routing").EnumerateArray());
        var item = Assert.Single(reply.Json.GetProperty("unfulfilled_items").EnumerateArray());
        Assert.Equal(cane, item.GetProperty("product_code").GetString());
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY_IN_AREA", item.GetProperty("reason_code").GetString());
    }
}

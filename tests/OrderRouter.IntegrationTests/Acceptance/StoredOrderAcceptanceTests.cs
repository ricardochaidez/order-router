using OrderRouter.IntegrationTests.Authentication;
using OrderRouter.IntegrationTests.Routing;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-023 AC9: each sample order posted to each path can be read back with <c>GET</c> on its <c>Location</c> as the
/// same caller, and the stored decision equals the POST response. The alias <c>Location</c> points at the v1 GET
/// (ADR-0004, ADR-0007).
/// </summary>
public class StoredOrderAcceptanceTests(ReferenceDataFixture fixture)
{
    private readonly AcceptanceApi api = new(fixture);

    private static readonly string[] decisionFields = ["id", "order_id", "feasible", "status", "routing", "unfulfilled_items", "warnings", "errors"];

    public static TheoryData<string, string> SampleOrdersOnEachPath()
    {
        var data = new TheoryData<string, string>();

        foreach (var order in new[] { "ORD-001", "ORD-002", "ORD-003" })
        {
            foreach (var path in new[] { "/api/v1/route", "/api/route" })
            {
                data.Add(order, path);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleOrdersOnEachPath))]
    public async Task Get_LocationOfASampleOrder_ReturnsTheDecisionFromPost(string sample, string path)
    {
        // AC9
        var orderId = AcceptanceApi.NewOrderId();
        var body = sample switch
        {
            "ORD-001" => SampleOrderAcceptanceTests.Ord001(orderId),
            "ORD-002" => SampleOrderAcceptanceTests.Ord002(orderId),
            _ => SampleOrderAcceptanceTests.Ord003(orderId),
        };
        var caller = TestJwt.ForSubject($"caller-{Guid.NewGuid():N}");
        await using var factory = api.CreateFactory();
        var post = await AcceptanceApi.PostCreatedAsync(factory, path, body, caller);

        var location = Assert.IsType<string>(post.Location);
        Assert.StartsWith("/api/v1/route/", new Uri(location, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(location).AbsolutePath : location, StringComparison.Ordinal);
        var get = await AcceptanceApi.GetAsync(factory, location, caller);

        Assert.Equal(System.Net.HttpStatusCode.OK, get.Status);
        foreach (var name in decisionFields)
        {
            Assert.Equal(post.Json.GetProperty(name).GetRawText(), get.Json.GetProperty(name).GetRawText());
        }
    }
}

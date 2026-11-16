using System.Net;
using System.Text.Json;
using OrderRouter.IntegrationTests.Authentication;
using OrderRouter.IntegrationTests.Routing;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-023 AC8: requests with no token, a bad token and each invalid field, through the real host and real routing
/// use case on both paths. A rejected request must store nothing (<c>routing_orders</c> has no row for its
/// <c>order_id</c>). The read-side checks (GET with no token, GET of another caller's order) are already covered by
/// <c>GetStoredOrderApiTests</c> on the real host, so they are not repeated here.
/// </summary>
public class RequestRejectionAcceptanceTests(ReferenceDataFixture fixture)
{
    private readonly AcceptanceApi api = new(fixture);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string validItems = """[ { "product_code": "WC-STD-001", "quantity": 1 } ]""";

    private static string Raw(string orderId, string zip = "\"10015\"", string items = validItems)
        => $$"""{ "order_id": {{orderId}}, "customer_zip": {{zip}}, "mail_order": false, "items": {{items}} }""";

    private async Task<string> StoredCountAsync(string orderId)
        => Assert.Single(await fixture.QueryAsync($"SELECT count(*) FROM routing_orders WHERE order_id = '{orderId}'", Token));

    private static string[] Errors(JsonElement json) => [.. json.GetProperty("errors").EnumerateArray().Select(error => error.GetString()!)];

    private static void AssertRejectionEnvelope(AcceptanceApi.Reply reply)
    {
        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.False(reply.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal("not_routed", reply.Json.GetProperty("status").GetString());
        Assert.Empty(reply.Json.GetProperty("routing").EnumerateArray());
        Assert.Empty(reply.Json.GetProperty("unfulfilled_items").EnumerateArray());
        Assert.Empty(reply.Json.GetProperty("warnings").EnumerateArray());
        Assert.NotEmpty(Errors(reply.Json));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_NoToken_Returns401WithChallengeAndStoresNothing(string path)
    {
        var orderId = AcceptanceApi.NewOrderId();
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.SendAsync(factory, RouteApiFactory.Post(path, Raw($"\"{orderId}\""), authenticated: false));

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Contains(reply.Response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
        Assert.Equal("0", await StoredCountAsync(orderId));
    }

    public static TheoryData<string, string> BadTokensOnEachPath()
    {
        var data = new TheoryData<string, string>();
        var tokens = new Dictionary<string, string>
        {
            ["malformed"] = "not-a-jwt",
            ["wrong-signature"] = TestJwt.BadSignature(),
            ["expired"] = TestJwt.Expired(),
            ["wrong-audience"] = TestJwt.WrongAudience(),
            ["wrong-issuer"] = TestJwt.WrongIssuer(),
        };

        foreach (var path in new[] { "/api/v1/route", "/api/route" })
        {
            foreach (var (kind, token) in tokens)
            {
                data.Add(path, $"{kind}|{token}");
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadTokensOnEachPath))]
    public async Task Post_BadToken_Returns401WithChallengeAndStoresNothing(string path, string kindAndToken)
    {
        var token = kindAndToken[(kindAndToken.IndexOf('|', StringComparison.Ordinal) + 1)..];
        var orderId = AcceptanceApi.NewOrderId();
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostAsync(factory, path, Raw($"\"{orderId}\""), token);

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.NotEmpty(reply.Response.Headers.WwwAuthenticate);
        Assert.Equal("0", await StoredCountAsync(orderId));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_BadTokenAndInvalidBody_Returns401NotTheValidationEnvelope(string path)
    {
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostAsync(factory, path, "{ not json", TestJwt.Expired());

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.DoesNotContain("feasible", reply.Text, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string?, string, string, string> InvalidFieldsOnEachPath()
    {
        const string item = """{ "product_code": "WC-STD-001", "quantity": 1 }""";
        var cases = new (string Name, string? OrderId, string Zip, string Items, string ErrorPrefix)[]
        {
            ("missing-order-id", (string?)null, "\"10015\"", $"[ {item} ]", "order_id "),
            ("blank-order-id", "\"   \"", "\"10015\"", $"[ {item} ]", "order_id "),
            ("missing-zip", "ID", "null", $"[ {item} ]", "customer_zip "),
            ("short-zip", "ID", "\"1001\"", $"[ {item} ]", "customer_zip "),
            ("long-zip", "ID", "\"100150\"", $"[ {item} ]", "customer_zip "),
            ("non-digit-zip", "ID", "\"1001a\"", $"[ {item} ]", "customer_zip "),
            ("numeric-zip", "ID", "10015", $"[ {item} ]", "The request body "),
            ("null-items", "ID", "\"10015\"", "null", "items"),
            ("empty-items", "ID", "\"10015\"", "[]", "items"),
            ("zero-quantity", "ID", "\"10015\"", """[ { "product_code": "WC-STD-001", "quantity": 0 } ]""", "items[0].quantity "),
            ("negative-quantity", "ID", "\"10015\"", """[ { "product_code": "WC-STD-001", "quantity": -2 } ]""", "items[0].quantity "),
            ("missing-quantity", "ID", "\"10015\"", """[ { "product_code": "WC-STD-001" } ]""", "items[0].quantity "),
            ("blank-product-code", "ID", "\"10015\"", """[ { "product_code": " ", "quantity": 1 } ]""", "items[0].product_code "),
        };
        var data = new TheoryData<string, string, string?, string, string, string>();

        foreach (var path in new[] { "/api/v1/route", "/api/route" })
        {
            foreach (var (name, orderId, zip, items, prefix) in cases)
            {
                data.Add(path, name, orderId, zip, items, prefix);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidFieldsOnEachPath))]
    public async Task Post_InvalidField_Returns400EnvelopeNamingTheFieldAndStoresNothing(string path, string name, string? orderIdJson, string zip, string items, string errorPrefix)
    {
        var orderId = AcceptanceApi.NewOrderId();

        // "ID" stands for a real, unique order id so the no-store check can look it up.
        var body = orderIdJson switch
        {
            null => $$"""{ "customer_zip": {{zip}}, "mail_order": false, "items": {{items}} }""",
            "ID" => Raw($"\"{orderId}\"", zip, items),
            var other => Raw(other, zip, items),
        };
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostAsync(factory, path, body);

        AssertRejectionEnvelope(reply);
        Assert.Single(Errors(reply.Json));
        Assert.StartsWith(errorPrefix, Errors(reply.Json)[0], StringComparison.Ordinal);

        // A wrong JSON type fails binding, so the message names the field by its JSON path instead of starting with it.
        if (name == "numeric-zip")
        {
            Assert.Contains("customer_zip", Errors(reply.Json)[0], StringComparison.Ordinal);
        }

        Assert.Equal("0", await StoredCountAsync(orderId));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_MalformedJson_Returns400EnvelopeWithoutParserInternals(string path)
    {
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostAsync(factory, path, "{ not json");

        AssertRejectionEnvelope(reply);
        var error = Assert.Single(Errors(reply.Json));
        Assert.DoesNotContain("System.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber", error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_SeveralInvalidFields_Returns400ListingEveryError(string path)
    {
        await using var factory = api.CreateFactory();

        var reply = await AcceptanceApi.PostAsync(
            factory,
            path,
            """{ "customer_zip": "x", "items": [ { "product_code": " ", "quantity": 0 }, { "quantity": 2 } ] }""");

        AssertRejectionEnvelope(reply);
        Assert.Equal(
        [
            "order_id is required and must not be blank.",
            "customer_zip must be exactly 5 digits.",
            "items[0].product_code is required and must not be blank.",
            "items[0].quantity must be at least 1.",
            "items[1].product_code is required and must not be blank.",
        ], Errors(reply.Json));
    }
}

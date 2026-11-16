using System.Net;
using System.Text.Json;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>AC1–AC7 and AC9 on both route paths (ADR-0004: the alias cannot drift from v1).</summary>
public class RouteValidationTests(ReferenceDataFixture fixture)
{
    private static string Body(string orderId = "\"ORD-1\"", string zip = "\"10015\"", string items = """[ { "product_code": "WC-STD-001", "quantity": 1 } ]""")
        => $$"""{ "order_id": {{orderId}}, "customer_zip": {{zip}}, "mail_order": false, "items": {{items}} }""";

    private async Task<(HttpStatusCode Status, JsonElement Json, RecordingRouteUseCase UseCase)> PostAsync(string path, string? body, string contentType = "application/json")
    {
        var useCase = new RecordingRouteUseCase();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, body, contentType: contentType), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone(), useCase);
    }

    private static string[] Errors(JsonElement json) => [.. json.GetProperty("errors").EnumerateArray().Select(error => error.GetString()!)];

    private static void AssertEnvelope(JsonElement json)
    {
        Assert.False(json.GetProperty("feasible").GetBoolean());
        Assert.Equal("not_routed", json.GetProperty("status").GetString());

        // AC9: every collection is present and is an array, never null.
        foreach (var name in new[] { "routing", "unfulfilled_items", "warnings", "errors" })
        {
            Assert.Equal(JsonValueKind.Array, json.GetProperty(name).ValueKind);
        }

        Assert.False(json.TryGetProperty("id", out _));
        Assert.False(json.TryGetProperty("order_id", out _));
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_MissingZipAndEmptyItems_Returns400WithAllErrors(string path)
    {
        var (status, json, useCase) = await PostAsync(path, """{ "order_id": "ORD-1", "items": [] }""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertEnvelope(json);
        var errors = Errors(json);
        Assert.Contains(errors, error => error.StartsWith("customer_zip ", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("items ", StringComparison.Ordinal));
        Assert.Empty(useCase.Commands);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_EverythingInvalid_ListsEveryErrorInRequestOrder(string path)
    {
        var (status, json, _) = await PostAsync(path, """{ "customer_zip": "x", "items": [ { "product_code": " ", "quantity": 0 }, { "quantity": 2 } ] }""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(
        [
            "order_id is required and must not be blank.",
            "customer_zip must be exactly 5 digits.",
            "items[0].product_code is required and must not be blank.",
            "items[0].quantity must be at least 1.",
            "items[1].product_code is required and must not be blank.",
        ], Errors(json));
    }

    [Theory]
    [InlineData("/api/v1/route", "2130")]
    [InlineData("/api/v1/route", "213000")]
    [InlineData("/api/v1/route", "21a30")]
    [InlineData("/api/v1/route", " 2130")]
    [InlineData("/api/v1/route", "02130\\n")]
    [InlineData("/api/route", "2130")]
    [InlineData("/api/route", "213000")]
    [InlineData("/api/route", "21a30")]
    [InlineData("/api/route", " 2130")]
    [InlineData("/api/route", "02130\\n")]
    public async Task Post_BadZip_Returns400NamingCustomerZip(string path, string zip)
    {
        var (status, json, useCase) = await PostAsync(path, Body(zip: $"\"{zip}\""));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(["customer_zip must be exactly 5 digits."], Errors(json));
        Assert.Empty(useCase.Commands);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_ZipNotAString_Returns400(string path)
    {
        var (status, json, _) = await PostAsync(path, Body(zip: "10015"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertEnvelope(json);
        Assert.Contains("customer_zip", Errors(json).Single(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_QuantityZeroNegativeOrMissing_Returns400(string path)
    {
        foreach (var item in new[] { """{ "product_code": "A", "quantity": 0 }""", """{ "product_code": "A", "quantity": -1 }""", """{ "product_code": "A" }""" })
        {
            var (status, json, useCase) = await PostAsync(path, Body(items: $"[ {item} ]"));

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.StartsWith("items[0].quantity ", Errors(json).Single(), StringComparison.Ordinal);
            Assert.Empty(useCase.Commands);
        }
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_BlankProductCodeOrOrderId_Returns400(string path)
    {
        var (productStatus, productJson, _) = await PostAsync(path, Body(items: """[ { "product_code": "  ", "quantity": 1 } ]"""));
        var (orderStatus, orderJson, _) = await PostAsync(path, Body(orderId: "\"\""));
        var (whitespaceStatus, whitespaceJson, _) = await PostAsync(path, Body(orderId: "\"   \""));

        Assert.Equal(HttpStatusCode.BadRequest, productStatus);
        Assert.StartsWith("items[0].product_code ", Errors(productJson).Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, orderStatus);
        Assert.StartsWith("order_id ", Errors(orderJson).Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, whitespaceStatus);
        Assert.StartsWith("order_id ", Errors(whitespaceJson).Single(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_ItemsNullEmptyMissingOrWithNullEntry_Returns400(string path)
    {
        foreach (var items in new[] { "null", "[]", "[ null ]" })
        {
            var (status, json, useCase) = await PostAsync(path, Body(items: items));

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.All(Errors(json), error => Assert.StartsWith("items", error, StringComparison.Ordinal));
            Assert.Empty(useCase.Commands);
        }

        var (missingStatus, _, _) = await PostAsync(path, """{ "order_id": "A", "customer_zip": "10015" }""");
        Assert.Equal(HttpStatusCode.BadRequest, missingStatus);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_MalformedJsonEmptyBodyOrWrongType_Returns400EnvelopeWithoutParserInternals(string path)
    {
        var bodies = new string?[]
        {
            "{ not json",
            "",
            null,
            "[]",
            """{ "order_id": "A", "customer_zip": "10015", "items": [ { "product_code": "A", "quantity": "abc" } ] }""",
            """{ "order_id": "A", "customer_zip": "10015", "mail_order": "yes", "items": [ { "product_code": "A", "quantity": 1 } ] }""",
        };

        foreach (var body in bodies)
        {
            var (status, json, useCase) = await PostAsync(path, body);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            AssertEnvelope(json);
            var error = Assert.Single(Errors(json));
            Assert.StartsWith("The request body ", error, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", error, StringComparison.Ordinal);
            Assert.DoesNotContain("LineNumber", error, StringComparison.Ordinal);
            Assert.Empty(useCase.Commands);
        }
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_WrongTypeInItem_NamesTheJsonPath(string path)
    {
        var (_, json, _) = await PostAsync(path, Body(items: """[ { "product_code": "A", "quantity": "abc" } ]"""));

        Assert.Contains("items[0].quantity", Errors(json).Single(), StringComparison.Ordinal);
    }

    // ASP.NET Core answers a non-JSON content type with 415 before binding (RFC 9110), so it never reaches validation.
    // This is the one failure that is not the v1 envelope; the plan's "wrong content type -> envelope" was not achievable
    // without reading the body by hand.
    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_WrongContentType_Returns415AndRoutingDoesNotRun(string path)
    {
        var useCase = new RecordingRouteUseCase();
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, Body(), contentType: "text/plain"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(useCase.Commands);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_ValidOrderWithExtraFields_Returns200AndRoutesTheOrder(string path)
    {
        var (status, json, useCase) = await PostAsync(path, RouteApiFactory.ValidBody);

        Assert.Equal(HttpStatusCode.OK, status);
        var command = Assert.Single(useCase.Commands);
        Assert.Equal("ORD-EXAMPLE", command.OrderId);
        Assert.Equal("10015", command.CustomerZip.Value);
        Assert.False(command.MailOrder);
        Assert.Equal(["WC-STD-001", "OX-PORT-024"], command.Lines.Select(line => line.ProductCode).ToArray());

        // AC9 on the success body too.
        foreach (var name in new[] { "routing", "unfulfilled_items", "warnings", "errors" })
        {
            Assert.Equal(JsonValueKind.Array, json.GetProperty(name).ValueKind);
        }

        Assert.Equal("ORD-EXAMPLE", json.GetProperty("order_id").GetString());
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_MailOrderOmitted_DefaultsToFalse(string path)
    {
        var (status, _, useCase) = await PostAsync(path, """{ "order_id": "A", "customer_zip": "10015", "items": [ { "product_code": "A", "quantity": 1 } ] }""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(Assert.Single(useCase.Commands).MailOrder);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_UnknownProductCode_PassesValidationAndReachesRouting(string path)
    {
        var (status, _, useCase) = await PostAsync(path, Body(items: """[ { "product_code": "XX-UNKNOWN-999", "quantity": 1 } ]"""));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("XX-UNKNOWN-999", Assert.Single(Assert.Single(useCase.Commands).Lines).ProductCode);
    }

    [Theory]
    [MemberData(nameof(RouteApiFactory.Paths), MemberType = typeof(RouteApiFactory))]
    public async Task Post_UseCaseReturnsRouted_SerializesSnakeCaseEnvelope(string path)
    {
        var routed = new RouteResponse
        {
            OrderId = "ORD-1",
            Status = RoutingStatus.Routed,
            Routing = [new SupplierRouting("SUP-1", "Care", [new RoutedItem("WC-STD-001", 1, "wheelchair", "local")])],
        };
        var useCase = new RecordingRouteUseCase(routed);
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(RouteApiFactory.Post(path, Body()), TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        Assert.True(json.GetProperty("feasible").GetBoolean());
        Assert.Equal("routed", json.GetProperty("status").GetString());
        Assert.Equal("local", json.GetProperty("routing")[0].GetProperty("items")[0].GetProperty("fulfillment_mode").GetString());
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderRouter.IntegrationTests.Authentication;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>
/// OR-028 acceptance: <c>GET /api/v1/route/{id}</c> reads a stored order back for the caller who created it (FR-21,
/// SC-31). Every test uses its own <c>order_id</c>, because the shared database accumulates stored orders.
/// </summary>
public class GetStoredOrderApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string alice = TestJwt.ForSubject("alice");
    private static readonly string bob = TestJwt.ForSubject("bob");

    private static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    private static string Body(string orderId, string zip, params (string Code, int Quantity)[] items)
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "{{zip}}", "mail_order": false,
              "items": [ {{string.Join(", ", items.Select(item => $$"""{ "product_code": "{{item.Code}}", "quantity": {{item.Quantity}} }"""))}} ] }
            """;

    private static async Task<(HttpResponseMessage Response, string Text, JsonElement Json)> SendAsync(WebApplicationFactory<Program> factory, HttpRequestMessage request)
    {
        using var client = factory.CreateClient();
        var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        var json = text.Length > 0 && text[0] == '{' ? JsonDocument.Parse(text).RootElement.Clone() : default;

        return (response, text, json);
    }

    private static async Task<(string Location, JsonElement Json)> PostAsync(WebApplicationFactory<Program> factory, string body, string? token = null)
    {
        var (response, _, json) = await SendAsync(factory, RouteApiFactory.Post("/api/v1/route", body, token: token ?? alice));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (response.Headers.Location!.OriginalString, json);
    }

    // trace_id differs on every response; everything else in a 404 must be identical.
    private static string WithoutTrace(string text) => Regex.Replace(text, "\"trace_id\":\"[^\"]*\"", string.Empty);

    private WebApplicationFactory<Program> Factory() => RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

    // Scoped to one order_id: other tests add orders to the shared database at the same time.
    private async Task<string> CountsAsync(string orderId)
        => string.Join(
            "|",
            await fixture.QueryAsync(
                $"SELECT count(*) || ',' || coalesce(sum((SELECT count(*) FROM routing_order_items i WHERE i.routing_order_id = o.id)), 0) FROM routing_orders o WHERE o.order_id = '{orderId}'",
                Token));

    // AC1
    [Fact]
    public async Task Get_OwnOrder_Returns200WithTheStoredRequestAndTheSameDecisionAsPost()
    {
        var orderId = NewOrderId();
        await using var factory = Factory();
        var (location, post) = await PostAsync(factory, Body(orderId, "77059", ("HB-FUL-018", 1), ("PL-ELEC-043", 1), ("CM-BED-048", 1), ("BP-AUTO-077", 1)));

        var (response, _, get) = await SendAsync(factory, RouteApiFactory.Get(location, token: alice));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(post.GetProperty("id").GetString(), get.GetProperty("id").GetString());
        Assert.Equal(orderId, get.GetProperty("order_id").GetString());
        Assert.Equal("77059", get.GetProperty("customer_zip").GetString());
        Assert.False(get.GetProperty("mail_order").GetBoolean());
        Assert.Equal(
            ["HB-FUL-018", "PL-ELEC-043", "CM-BED-048", "BP-AUTO-077"],
            get.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("product_code").GetString()));
        Assert.All(get.GetProperty("items").EnumerateArray(), item => Assert.Equal(1, item.GetProperty("quantity").GetInt32()));
        Assert.True(get.TryGetProperty("created_at", out _));

        foreach (var name in new[] { "feasible", "status", "routing", "unfulfilled_items", "warnings", "errors" })
        {
            Assert.Equal(post.GetProperty(name).GetRawText(), get.GetProperty(name).GetRawText());
        }
    }

    // AC2: the tie-break is random, but a read must show the supplier POST chose, every time.
    [Fact]
    public async Task Get_TiedSuppliers_AlwaysShowsTheSupplierFromPost()
    {
        await using var factory = Factory();
        var (location, post) = await PostAsync(factory, Body(NewOrderId(), "00003", ("CN-STD-063", 1)));
        var chosen = post.GetProperty("routing")[0].GetProperty("supplier_id").GetString();
        Assert.Contains(chosen, new[] { "SUP-T003", "SUP-T004" });

        for (var i = 0; i < 10; i++)
        {
            var (response, _, get) = await SendAsync(factory, RouteApiFactory.Get(location, token: alice));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(chosen, get.GetProperty("routing")[0].GetProperty("supplier_id").GetString());
        }
    }

    // AC3: the default arm is all-or-nothing, so the routable line is held back but still listed.
    [Fact]
    public async Task Get_OrderHeldBackByAllOrNothing_ListsEveryRequestedLine()
    {
        await using var factory = Factory();
        var (location, _) = await PostAsync(factory, Body(NewOrderId(), "10015", ("WC-STD-001", 2), ("XX-UNKNOWN-999", 1), ("OX-PORT-024", 3)));

        var (_, _, get) = await SendAsync(factory, RouteApiFactory.Get(location, token: alice));

        Assert.Equal("not_routed", get.GetProperty("status").GetString());
        Assert.False(get.GetProperty("feasible").GetBoolean());
        Assert.Empty(get.GetProperty("routing").EnumerateArray());
        Assert.Equal(
            [("WC-STD-001", 2), ("XX-UNKNOWN-999", 1), ("OX-PORT-024", 3)],
            get.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("product_code").GetString()!, item.GetProperty("quantity").GetInt32())));
        Assert.Equal("XX-UNKNOWN-999", Assert.Single(get.GetProperty("unfulfilled_items").EnumerateArray()).GetProperty("product_code").GetString());
    }

    // AC5, SC-31: another caller gets exactly what an unknown id gets.
    [Fact]
    public async Task Get_AnotherCallersOrder_Returns404WithTheSameBodyAsAnUnknownId()
    {
        await using var factory = Factory();
        var (location, _) = await PostAsync(factory, Body(NewOrderId(), "10015", ("WC-STD-001", 1)));

        var (other, otherText, _) = await SendAsync(factory, RouteApiFactory.Get(location, token: bob));
        var (unknown, unknownText, _) = await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{Guid.CreateVersion7()}", token: bob));

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(WithoutTrace(unknownText), WithoutTrace(otherText));
        Assert.Contains("Order not found", otherText, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", otherText, StringComparison.Ordinal);
    }

    // AC6
    [Theory]
    [InlineData("abc")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    [InlineData("12345")]
    public async Task Get_InvalidId_Returns404WithTheSameBodyAsAnUnknownId(string id)
    {
        await using var factory = Factory();
        var (_, unknownText, _) = await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{Guid.CreateVersion7()}", token: alice));

        var (response, text, _) = await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{Uri.EscapeDataString(id)}", token: alice));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(WithoutTrace(unknownText), WithoutTrace(text));
    }

    [Fact]
    public async Task Get_RandomUnknownUuid_Returns404()
    {
        await using var factory = Factory();

        var (response, _, json) = await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{Guid.NewGuid()}", token: alice));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, json.GetProperty("status").GetInt32());
    }

    // AC7: authentication runs first, also for an id that is not a UUID.
    [Theory]
    [InlineData("abc")]
    [InlineData("0193a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5b")]
    public async Task Get_NoCredentials_Returns401WithWwwAuthenticate(string id)
    {
        await using var factory = Factory();

        var (response, _, _) = await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{id}", authenticated: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
    }

    // AC8: the alias is POST-only.
    [Fact]
    public async Task Get_OnTheAliasPath_Returns404EvenForARealOrderAndAValidCaller()
    {
        await using var factory = Factory();
        var (location, _) = await PostAsync(factory, Body(NewOrderId(), "10015", ("WC-STD-001", 1)));
        var id = location[(location.LastIndexOf('/') + 1)..];

        var (response, _, _) = await SendAsync(factory, RouteApiFactory.Get($"/api/route/{id}", token: alice));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Decided in OR-027: a token with no caller claim can POST but can never read the order back.
    [Fact]
    public async Task Get_TokenWithNoCallerClaim_Returns404EvenForItsOwnOrder()
    {
        await using var factory = Factory();
        var (location, _) = await PostAsync(factory, Body(NewOrderId(), "10015", ("WC-STD-001", 1)), token: TestJwt.WithoutCaller());

        var (response, _, _) = await SendAsync(factory, RouteApiFactory.Get(location, token: TestJwt.WithoutCaller()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // GET is safe: it changes nothing and never routes again.
    [Fact]
    public async Task Get_Repeatedly_LeavesTheTablesUnchanged()
    {
        await using var factory = Factory();
        var orderId = NewOrderId();
        var (location, _) = await PostAsync(factory, Body(orderId, "10015", ("WC-STD-001", 1)));
        var before = await CountsAsync(orderId);
        Assert.Equal("1,1", before);

        for (var i = 0; i < 3; i++)
        {
            await SendAsync(factory, RouteApiFactory.Get(location, token: alice));
            await SendAsync(factory, RouteApiFactory.Get($"/api/v1/route/{Guid.NewGuid()}", token: alice));
        }

        Assert.Equal(before, await CountsAsync(orderId));
    }

    // §9.5: created_at is ISO 8601 UTC, written with a Z.
    [Fact]
    public async Task Get_OwnOrder_SerializesCreatedAtAsIso8601Utc()
    {
        await using var factory = Factory();
        var (location, _) = await PostAsync(factory, Body(NewOrderId(), "10015", ("WC-STD-001", 1)));

        var (_, _, get) = await SendAsync(factory, RouteApiFactory.Get(location, token: alice));

        var createdAt = get.GetProperty("created_at").GetString()!;
        Assert.EndsWith("Z", createdAt, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse(createdAt, out var parsed));
        Assert.True(parsed > DateTimeOffset.UtcNow.AddMinutes(-5));
    }
}

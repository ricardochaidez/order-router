using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-019 acceptance on the real host: a temp <c>experiments.json</c> puts every caller in the EXP-3 treatment arm.
/// A valid POST answers 201 Created (OR-027).
/// </summary>
public class PartialRoutingApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string exp3AllTreated = """
        { "Experiments": { "Exp3PartialRouting": { "Enabled": true, "TreatmentPercentage": 100, "Salt": "exp3",
            "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] } } } }
        """;

    private const string routable = """{ "product_code": "WC-STD-001", "quantity": 1 }""";

    private const string unknown = """{ "product_code": "XX-UNKNOWN-999", "quantity": 3 }""";

    private static string Order(string zip, params string[] items)
        => $$"""{ "order_id": "ORD-P", "customer_zip": "{{zip}}", "mail_order": false, "items": [ {{string.Join(", ", items)}} ] }""";

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(string body)
    {
        var directory = Directory.CreateTempSubdirectory("experiments-");

        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "experiments.json"), exp3AllTreated);
            using var provider = new PhysicalFileProvider(directory.FullName);
            await using var factory = AuthApiFactory.Create(fixture.ConnectionString)
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddJsonFile(provider, "experiments.json", optional: false, reloadOnChange: false)));
            using var client = factory.CreateClient();

            using var response = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", body), Token);
            var text = await response.Content.ReadAsStringAsync(Token);

            return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Post_OneRoutableAndOneUnknownLine_IsPartiallyRouted()
    {
        // AC1: 201 Created since OR-027.
        var (status, json) = await PostAsync(Order("10015", routable, unknown));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("partially_routed", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("feasible").GetBoolean());
        var group = Assert.Single(json.GetProperty("routing").EnumerateArray());
        Assert.Equal("WC-STD-001", Assert.Single(group.GetProperty("items").EnumerateArray()).GetProperty("product_code").GetString());
        var item = Assert.Single(json.GetProperty("unfulfilled_items").EnumerateArray());
        Assert.Equal("XX-UNKNOWN-999", item.GetProperty("product_code").GetString());
        Assert.Equal("UNKNOWN_PRODUCT", item.GetProperty("reason_code").GetString());
        Assert.Equal(item.GetProperty("message").GetString(), Assert.Single(json.GetProperty("errors").EnumerateArray()).GetString());
        Assert.Empty(json.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Post_NothingRoutable_IsNotRouted()
    {
        // AC2
        var (status, json) = await PostAsync(Order("10015", unknown));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("not_routed", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("feasible").GetBoolean());
        Assert.Empty(json.GetProperty("routing").EnumerateArray());
        Assert.Single(json.GetProperty("unfulfilled_items").EnumerateArray());
    }

    [Fact]
    public async Task Post_FullyRoutable_IsRouted()
    {
        // AC3
        var (status, json) = await PostAsync(Order("10015", routable));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("routed", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("feasible").GetBoolean());
        Assert.Empty(json.GetProperty("unfulfilled_items").EnumerateArray());
    }

    [Fact]
    public async Task Post_InvalidZip_Returns400AndNothingIsRouted()
    {
        // AC4: validation runs before routing, whatever the arm.
        var (status, json) = await PostAsync(Order("2130", routable, unknown));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("not_routed", json.GetProperty("status").GetString());
        Assert.Empty(json.GetProperty("routing").EnumerateArray());
        Assert.Equal(["customer_zip must be exactly 5 digits."], json.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }
}

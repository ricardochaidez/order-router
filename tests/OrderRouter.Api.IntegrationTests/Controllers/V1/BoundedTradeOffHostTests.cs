using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-017 acceptance on the real host: real use case, real policy factory, real plan search and a temp
/// <c>experiments.json</c> that reloads. ZIP 00007 holds the seeded sentinels SUP-T007 (rating 6, wheelchair and cane)
/// and SUP-T008 / SUP-T009 (rating 10 each, one category): one shipment at 6.0 against two at 10.0, an uplift of 4.0.
/// </summary>
public class BoundedTradeOffHostTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(30);

    private static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    private static string Body()
        => $$"""
            { "order_id": "{{NewOrderId()}}", "customer_zip": "00007", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "CN-STD-063", "quantity": 1 } ] }
            """;

    private static string Exp1(bool enabled, decimal threshold)
        => $$"""
            { "Experiments": { "Exp1ConsolidationVsQuality": {
                "Enabled": {{(enabled ? "true" : "false")}}, "TreatmentPercentage": 100, "Salt": "exp1",
                "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] },
                "Parameters": { "QualityUpliftThreshold": {{threshold}} } } } }
            """;

    private WebApplicationFactory<Program> Host(TempExperimentsFile file)
        => AuthApiFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddJsonFile(file.Provider, "experiments.json", optional: false, reloadOnChange: true)));

    private static async Task<int> ShipmentsAsync(HttpClient client)
    {
        using var response = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", Body()), Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return JsonDocument.Parse(text).RootElement.GetProperty("routing").GetArrayLength();
    }

    private static async Task WaitUntilShipmentsAsync(HttpClient client, int expected, string because)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (await ShipmentsAsync(client) != expected)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {because}");
            await Task.Delay(100, Token);
        }
    }

    [Fact]
    public async Task Post_TreatmentUpliftAboveThreshold_RoutesTheExtraShipmentPlan()
    {
        // AC1
        using var file = new TempExperimentsFile(Exp1(enabled: true, threshold: 2m));
        await using var host = Host(file);
        using var client = host.CreateClient();

        Assert.Equal(2, await ShipmentsAsync(client));
    }

    [Fact]
    public async Task Post_ControlArm_RoutesTheFewestShipments()
    {
        // AC5: experiment disabled equals the written spec.
        using var file = new TempExperimentsFile(Exp1(enabled: false, threshold: 2m));
        await using var host = Host(file);
        using var client = host.CreateClient();

        Assert.Equal(1, await ShipmentsAsync(client));
    }

    [Fact]
    public async Task Post_ThresholdRaisedInTheFile_NextRequestRoutesTheStrictPriorityPlan()
    {
        // AC4 and AC2: no restart; a threshold above the 4.0 uplift keeps the single shipment.
        using var file = new TempExperimentsFile(Exp1(enabled: true, threshold: 2m));
        await using var host = Host(file);
        using var client = host.CreateClient();
        Assert.Equal(2, await ShipmentsAsync(client));

        file.Write(Exp1(enabled: true, threshold: 5m));

        await WaitUntilShipmentsAsync(client, 1, "the raised threshold to be applied");
    }

    [Fact]
    public async Task Post_InvalidThresholdWritten_KeepsTheLastGoodThreshold()
    {
        using var file = new TempExperimentsFile(Exp1(enabled: true, threshold: 2m));
        await using var host = Host(file);
        using var client = host.CreateClient();
        Assert.Equal(2, await ShipmentsAsync(client));

        file.Write(Exp1(enabled: true, threshold: 0m));
        await Task.Delay(1500, Token);

        Assert.Equal(2, await ShipmentsAsync(client));
    }
}

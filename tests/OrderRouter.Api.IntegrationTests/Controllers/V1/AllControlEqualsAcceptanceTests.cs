using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-024 AC2 (AB-03, SC-10): with every experiment in control the sentinel and sample orders give the OR-023 results,
/// both with an explicit all-control <c>experiments.json</c> and with the shipped file (all disabled). The expectations are
/// <see cref="Or023Expectations"/>, the same constants the OR-023 tests assert, on the shared seeded database.
/// </summary>
public class AllControlEqualsAcceptanceTests(ReferenceDataFixture fixture)
{
    private static async Task AssertCasesAsync(WebApplicationFactory<Program> factory)
    {
        foreach (var testCase in Or023Expectations.Cases)
        {
            var reply = await AcceptanceApi.PostCreatedAsync(factory, "/api/v1/route", testCase.Body(AcceptanceApi.NewOrderId()));
            var suppliers = reply.SupplierIds.Order(StringComparer.Ordinal).ToArray();

            Assert.True(reply.Json.GetProperty("feasible").GetBoolean(), testCase.Name);
            Assert.Equal("routed", reply.Json.GetProperty("status").GetString());

            if (testCase.Tied)
            {
                Assert.Contains(Assert.Single(suppliers), testCase.Suppliers);
            }
            else
            {
                Assert.Equal(testCase.Suppliers, suppliers);
            }
        }
    }

    [Fact]
    public async Task Post_ExplicitAllControlCombination_EqualsTheOr023Results()
    {
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(false, false, false));

        await AssertCasesAsync(host.Factory);

        var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", Or023Expectations.Cases[0].Body(AcceptanceApi.NewOrderId()));
        var arms = reply.Json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.All(arms.EnumerateObject(), arm => Assert.Equal("control", arm.Value.GetProperty("arm").GetString()));
    }

    [Fact]
    public async Task Post_ShippedExperimentsFile_EqualsTheOr023ResultsAndEveryArmIsControl()
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, useCase: null, exposeDiagnostics: true);

        await AssertCasesAsync(factory);

        var reply = await AcceptanceApi.PostCreatedAsync(factory, "/api/v1/route", Or023Expectations.Cases[0].Body(AcceptanceApi.NewOrderId()));
        var arms = reply.Json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.All(arms.EnumerateObject(), arm =>
        {
            Assert.Equal("control", arm.Value.GetProperty("arm").GetString());
            Assert.Equal("disabled", arm.Value.GetProperty("reason").GetString());
        });
    }
}

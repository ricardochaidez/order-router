using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// The scenarios of the README ("Sample orders" and "Experiments: control vs. treatment") run against the real host,
/// with each full request and response written to <c>&lt;results&gt;/responses/...</c> by <see cref="ResponseRecorder"/>
/// when <c>ORDERROUTER_RESULTS_DIR</c> is set (<c>scripts/run-tests.sh</c> does this). They also assert the documented
/// outcome, so the files and the pass/fail result always agree.
/// </summary>
/// <remarks>
/// Folders: <c>default-route</c> (every experiment off, the shipped default), <c>experiments/exp1|exp2|exp3</c> (one
/// experiment at treatment 100%, the others off, each next to its control) and <c>experiments/combinations</c> (all eight
/// arm combinations over the four sentinel orders, on the throwaway matrix database).
/// </remarks>
public class ResponseReportTests(ReferenceDataFixture fixture, MatrixDatabaseFixture matrix) : IClassFixture<MatrixDatabaseFixture>
{
    private const string path = "/api/v1/route";
    private const string wheelchair = "WC-STD-001";
    private const string walker = "WK-STD-009";
    private const string cane = "CN-STD-063";
    private const string rollator = "RL-STD-058";

    private static readonly ExperimentCombination allControl = new(false, false, false);

    private static readonly (string Name, string Description, Func<string, string> Body)[] sampleOrders =
    [
        ("ORD-001", "Wheelchair + oxygen, local: one shipment from the only local supplier that handles both.", SampleOrderAcceptanceTests.Ord001),
        ("ORD-002", "Four items, local: no supplier handles all four categories, so two shipments.", SampleOrderAcceptanceTests.Ord002),
        ("ORD-003", "CPAP and nebulizer, mail order: one shipment, a true tie broken by the (fixed) random source.", SampleOrderAcceptanceTests.Ord003),
    ];

    // Orders from the README experiment section, on the shared seeded data.
    private static string Exp1Order(string id) => AcceptanceApi.Body(id, "02101", false, ("BB-LUM-090", 1), ("CP-STD-031", 1));

    private static string Exp2Order(string id) => AcceptanceApi.Body(id, "02101", false, ("CP-STD-031", 1), ("GL-STD-081", 1));

    private static string Exp3Order(string id) => AcceptanceApi.Body(id, "10015", false, ("WC-STD-001", 1), ("OX-PORT-024", 1), ("OX-CONC-999", 1));

    private static async Task<AcceptanceApi.Reply> RouteAndRecordAsync(ExperimentArmsHost host, string group, string name, string description, string body, ExperimentCombination combination)
    {
        var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, path, body);
        ResponseRecorder.Record(group, name, description, "POST", path, body, (int)reply.Status, reply.Location, reply.Text, ExperimentArmsHost.Json(combination));

        return reply;
    }

    private static void AssertArms(AcceptanceApi.Reply reply, ExperimentCombination combination)
    {
        var arms = reply.Json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp1), arms.GetProperty("exp1_consolidation_vs_quality").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp2), arms.GetProperty("exp2_unrated_suppliers").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp3), arms.GetProperty("exp3_partial_routing").GetProperty("arm").GetString());
    }

    private static string[] Sorted(AcceptanceApi.Reply reply) => [.. reply.SupplierIds.Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Post_DefaultRoute_RecordsTheSampleOrdersWithEveryExperimentOff()
    {
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, allControl);
        var expected = new[] { Or023Expectations.Ord001, Or023Expectations.Ord002, Or023Expectations.Ord003Tied };

        for (var index = 0; index < sampleOrders.Length; index++)
        {
            var (name, description, body) = sampleOrders[index];
            var reply = await RouteAndRecordAsync(host, "default-route", name, description, body(AcceptanceApi.NewOrderId()), allControl);

            AssertArms(reply, allControl);
            Assert.Equal("routed", reply.Json.GetProperty("status").GetString());
            Assert.True(reply.Json.GetProperty("feasible").GetBoolean());

            // ORD-003 is a tie, so any one of the tied suppliers is right.
            if (index == 2)
            {
                Assert.Contains(Assert.Single(reply.SupplierIds), expected[index]);
            }
            else
            {
                Assert.Equal(expected[index], Sorted(reply));
            }
        }
    }

    [Fact]
    public async Task Post_Exp1_RecordsControlAndTreatment()
    {
        var treatment = new ExperimentCombination(true, false, false);
        await using var control = ExperimentArmsHost.Create(fixture.ConnectionString, allControl);
        await using var treated = ExperimentArmsHost.Create(fixture.ConnectionString, treatment);

        var a0 = await RouteAndRecordAsync(control, "experiments/exp1", "split-order-control", "EXP-1 control: always the fewest shipments.", Exp1Order(AcceptanceApi.NewOrderId()), allControl);
        var a1 = await RouteAndRecordAsync(treated, "experiments/exp1", "split-order-treatment", "EXP-1 treatment: a second shipment when quality rises by more than the 2.0 threshold.", Exp1Order(AcceptanceApi.NewOrderId()), treatment);
        var b0 = await RouteAndRecordAsync(control, "experiments/exp1", "ord-001-control", "ORD-001 under control.", SampleOrderAcceptanceTests.Ord001(AcceptanceApi.NewOrderId()), allControl);
        var b1 = await RouteAndRecordAsync(treated, "experiments/exp1", "ord-001-treatment", "ORD-001 under treatment: the gain is below the threshold, so it stays at one shipment.", SampleOrderAcceptanceTests.Ord001(AcceptanceApi.NewOrderId()), treatment);

        Assert.Equal(["SUP-0955"], a0.SupplierIds);
        Assert.Equal(["SUP-065", "SUP-0956"], Sorted(a1));
        Assert.Equal(Or023Expectations.Ord001, b0.SupplierIds);
        Assert.Equal(Or023Expectations.Ord001, b1.SupplierIds);
        AssertArms(a0, allControl);
        AssertArms(a1, treatment);
    }

    [Fact]
    public async Task Post_Exp2_RecordsControlAndTreatment()
    {
        var treatment = new ExperimentCombination(false, true, false);
        await using var control = ExperimentArmsHost.Create(fixture.ConnectionString, allControl);
        await using var treated = ExperimentArmsHost.Create(fixture.ConnectionString, treatment);

        var c = await RouteAndRecordAsync(control, "experiments/exp2", "unrated-supplier-control", "EXP-2 control: an unrated supplier counts as 0, so the rated one wins.", Exp2Order(AcceptanceApi.NewOrderId()), allControl);
        var t = await RouteAndRecordAsync(treated, "experiments/exp2", "unrated-supplier-treatment", "EXP-2 treatment: an unrated supplier counts as 6.5, so it beats the 4.8-rated one.", Exp2Order(AcceptanceApi.NewOrderId()), treatment);

        Assert.Equal(["SUP-0955"], c.SupplierIds);
        Assert.Equal(["SUP-0781"], t.SupplierIds);
        AssertArms(c, allControl);
        AssertArms(t, treatment);
    }

    [Fact]
    public async Task Post_Exp3_RecordsControlAndTreatment()
    {
        var treatment = new ExperimentCombination(false, false, true);
        await using var control = ExperimentArmsHost.Create(fixture.ConnectionString, allControl);
        await using var treated = ExperimentArmsHost.Create(fixture.ConnectionString, treatment);

        var c = await RouteAndRecordAsync(control, "experiments/exp3", "partial-routing-control", "EXP-3 control: one unknown product, so nothing is routed.", Exp3Order(AcceptanceApi.NewOrderId()), allControl);
        var t = await RouteAndRecordAsync(treated, "experiments/exp3", "partial-routing-treatment", "EXP-3 treatment: the known items route and only the unknown one is left unfulfilled.", Exp3Order(AcceptanceApi.NewOrderId()), treatment);

        Assert.Equal("not_routed", c.Json.GetProperty("status").GetString());
        Assert.Empty(c.SupplierIds);
        Assert.Equal("partially_routed", t.Json.GetProperty("status").GetString());
        Assert.Equal(Or023Expectations.Ord001, t.SupplierIds);
        Assert.False(t.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal("OX-CONC-999", Assert.Single(t.Json.GetProperty("unfulfilled_items").EnumerateArray()).GetProperty("product_code").GetString());
    }

    /// <summary>The eight combinations of <see cref="ExperimentCombinationMatrixTests"/>, with each response recorded.</summary>
    [Fact]
    public async Task Post_AllArmCombinations_RecordsEveryResponse()
    {
        foreach (var (number, combination) in ExperimentCombination.All())
        {
            await using var host = ExperimentArmsHost.Create(matrix.ConnectionString, combination);
            var label = $"combination-{number}-exp1-{ExperimentCombination.ArmName(combination.Exp1)}-exp2-{ExperimentCombination.ArmName(combination.Exp2)}-exp3-{ExperimentCombination.ArmName(combination.Exp3)}";
            (string Order, string Description, string Body)[] orders =
            [
                ("A", "ZIP 00007, wheelchair + cane (EXP-1 sensitive).", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00007", false, (wheelchair, 1), (cane, 1))),
                ("B", "ZIP 00050, rollator (EXP-2 sensitive).", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00050", false, (rollator, 1))),
                ("C", "ZIP 00098, wheelchair + walker (EXP-3 sensitive).", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00098", false, (wheelchair, 1), (walker, 1))),
                ("D", "ZIP 00051, wheelchair + cane (EXP-1 x EXP-2 interaction).", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00051", false, (wheelchair, 1), (cane, 1))),
            ];

            foreach (var (order, description, body) in orders)
            {
                var reply = await RouteAndRecordAsync(host, "experiments/combinations", $"{label}-order-{order}", $"Combination {number}, order {order}: {description}", body, combination);
                AssertArms(reply, combination);
            }
        }
    }
}

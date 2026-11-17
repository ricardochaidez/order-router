using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-024 AC1: every one of the 2 x 2 x 2 experiment arm combinations routes the same four sentinel orders on the
/// real host, with the arms pinned and the random source fixed (ADR-0001 negative consequences). The expectations
/// below are derived from the rules (Technical Requirements §7.3 and §8.1, ADR-0001), not from running the code.
/// </summary>
/// <remarks>
/// Orders (all <c>mail_order=false</c>, data from <see cref="MatrixDatabaseFixture"/> and the seeded sentinels):
/// <list type="bullet">
/// <item><b>A</b> ZIP 00007, wheelchair + cane. Only EXP-1 matters: control one shipment, <c>SUP-T007</c> (6.0); treatment two shipments, <c>SUP-T008</c> + <c>SUP-T009</c> (10.0, uplift 4.0 at least 2.0).</item>
/// <item><b>B</b> ZIP 00050, rollator. Only EXP-2 matters: control the rated <c>SUP-X001</c> (6.0 against 0); treatment the unrated <c>SUP-X002</c> (prior 6.5 is within the 0.5 band and ranks higher).</item>
/// <item><b>C</b> ZIP 00098, wheelchair + walker (<c>SUP-T014</c> serves wheelchairs only). Only EXP-3 matters: control <c>not_routed</c>; treatment <c>partially_routed</c> with the wheelchair on <c>SUP-T014</c>. Never <c>feasible</c>.</item>
/// <item><b>D</b> ZIP 00051, wheelchair + cane. The interaction: EXP-1 treatment buys an extra shipment (<c>SUP-X012</c> + <c>SUP-X013</c>, 8.0) only while the unrated generalist <c>SUP-X011</c> counts as 0 (uplift 8.0);
/// with the EXP-2 prior it counts as 6.5, the uplift is 1.5 (below 2.0) and the single shipment stays.</item>
/// </list>
/// <code>
/// #  EXP-1 EXP-2 EXP-3 | A              | B        | C               | D
/// 1  ctl   ctl   ctl   | T007           | X001     | not_routed      | X011
/// 2  ctl   ctl   trt   | T007           | X001     | partially_routed| X011
/// 3  ctl   trt   ctl   | T007           | X002     | not_routed      | X011
/// 4  ctl   trt   trt   | T007           | X002     | partially_routed| X011
/// 5  trt   ctl   ctl   | T008 + T009    | X001     | not_routed      | X012 + X013
/// 6  trt   ctl   trt   | T008 + T009    | X001     | partially_routed| X012 + X013
/// 7  trt   trt   ctl   | T008 + T009    | X002     | not_routed      | X011
/// 8  trt   trt   trt   | T008 + T009    | X002     | partially_routed| X011
/// </code>
/// </remarks>
public class ExperimentCombinationMatrixTests(MatrixDatabaseFixture fixture) : IClassFixture<MatrixDatabaseFixture>
{
    private const string wheelchair = "WC-STD-001";
    private const string walker = "WK-STD-009";
    private const string cane = "CN-STD-063";
    private const string rollator = "RL-STD-058";

    /// <summary>The documented result of one combination.</summary>
    private sealed record Expected(string[] OrderA, string OrderB, bool Exp3Treatment, string[] OrderD);

    private static readonly string[] one = ["SUP-T007"];
    private static readonly string[] two = ["SUP-T008", "SUP-T009"];
    private static readonly string[] x011 = ["SUP-X011"];
    private static readonly string[] x012x013 = ["SUP-X012", "SUP-X013"];

    private static readonly Expected[] table =
    [
        new(one, "SUP-X001", false, x011),
        new(one, "SUP-X001", true, x011),
        new(one, "SUP-X002", false, x011),
        new(one, "SUP-X002", true, x011),
        new(two, "SUP-X001", false, x012x013),
        new(two, "SUP-X001", true, x012x013),
        new(two, "SUP-X002", false, x011),
        new(two, "SUP-X002", true, x011),
    ];

    public static IEnumerable<TheoryDataRow<int, bool, bool, bool>> Combinations
        => ExperimentCombination.All().Select(row => new TheoryDataRow<int, bool, bool, bool>(row.Number, row.Combination.Exp1, row.Combination.Exp2, row.Combination.Exp3)
        {
            TestDisplayName = $"#{row.Number} EXP-1 {ExperimentCombination.ArmName(row.Combination.Exp1)}, EXP-2 {ExperimentCombination.ArmName(row.Combination.Exp2)}, EXP-3 {ExperimentCombination.ArmName(row.Combination.Exp3)}",
        });

    private static async Task<AcceptanceApi.Reply> RouteAsync(WebApplicationFactory<Program> factory, string zip, params (string Code, int Quantity)[] items)
        => await AcceptanceApi.PostCreatedAsync(factory, "/api/v1/route", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), zip, false, items));

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Post_EveryArmCombination_ReturnsItsDocumentedResult(int number, bool exp1, bool exp2, bool exp3)
    {
        var expected = table[number - 1];
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(exp1, exp2, exp3));
        var factory = host.Factory;

        // Order A (EXP-1) also proves the arms first, so a configuration mistake cannot pass silently.
        var a = await RouteAsync(factory, "00007", (wheelchair, 1), (cane, 1));
        var arms = a.Json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.Equal(ExperimentCombination.ArmName(exp1), arms.GetProperty("exp1_consolidation_vs_quality").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(exp2), arms.GetProperty("exp2_unrated_suppliers").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(exp3), arms.GetProperty("exp3_partial_routing").GetProperty("arm").GetString());
        Assert.Equal(expected.OrderA, a.SupplierIds.Order(StringComparer.Ordinal));
        Assert.Equal("routed", a.Json.GetProperty("status").GetString());
        Assert.True(a.Json.GetProperty("feasible").GetBoolean());
        Assert.Empty(a.Json.GetProperty("unfulfilled_items").EnumerateArray());

        var b = await RouteAsync(factory, "00050", (rollator, 1));
        Assert.Equal([expected.OrderB], b.SupplierIds);
        Assert.Equal("routed", b.Json.GetProperty("status").GetString());
        Assert.True(b.Json.GetProperty("feasible").GetBoolean());

        var c = await RouteAsync(factory, "00098", (wheelchair, 1), (walker, 1));
        Assert.False(c.Json.GetProperty("feasible").GetBoolean());
        var unfulfilled = c.Json.GetProperty("unfulfilled_items").EnumerateArray().ToArray();
        Assert.Equal(walker, Assert.Single(unfulfilled).GetProperty("product_code").GetString());

        if (expected.Exp3Treatment)
        {
            Assert.Equal("partially_routed", c.Json.GetProperty("status").GetString());
            Assert.Equal(["SUP-T014"], c.SupplierIds);
            Assert.Equal(wheelchair, Assert.Single(c.Items).GetProperty("product_code").GetString());
        }
        else
        {
            Assert.Equal("not_routed", c.Json.GetProperty("status").GetString());
            Assert.Empty(c.SupplierIds);
        }

        var d = await RouteAsync(factory, "00051", (wheelchair, 1), (cane, 1));
        Assert.Equal(expected.OrderD, d.SupplierIds.Order(StringComparer.Ordinal));
        Assert.Equal("routed", d.Json.GetProperty("status").GetString());
        Assert.True(d.Json.GetProperty("feasible").GetBoolean());
        Assert.Equal(2, d.Items.Length);
    }
}

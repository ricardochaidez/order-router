using System.Text.RegularExpressions;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-024 harness checks: the pinned host really puts each experiment on the intended arm, stores a
/// <c>config_version</c>, and routes tie-breaks through the fixed random source. Without these a config mistake
/// could make a matrix test pass on the wrong arm.
/// </summary>
public partial class ExperimentArmsHostTests(MatrixDatabaseFixture fixture) : IClassFixture<MatrixDatabaseFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string cane = "CN-STD-063";

    [GeneratedRegex("^[0-9a-f]{12}$")]
    private static partial Regex ConfigVersionPattern();

    public static TheoryData<int, bool, bool, bool> Combinations
        => new([.. ExperimentCombination.All().Select(row => (row.Number, row.Combination.Exp1, row.Combination.Exp2, row.Combination.Exp3))]);

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Post_PinnedCombination_ReportsTheIntendedArmsAndStoresAConfigVersion(int number, bool exp1, bool exp2, bool exp3)
    {
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(exp1, exp2, exp3));
        var orderId = AcceptanceApi.NewOrderId();

        var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", AcceptanceApi.Body(orderId, "00003", false, (cane, 1)));

        var arms = reply.Json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.Equal(ExperimentCombination.ArmName(exp1), arms.GetProperty("exp1_consolidation_vs_quality").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(exp2), arms.GetProperty("exp2_unrated_suppliers").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(exp3), arms.GetProperty("exp3_partial_routing").GetProperty("arm").GetString());
        Assert.True(number is >= 1 and <= 8);
        var version = Assert.Single(await fixture.QueryAsync($"SELECT config_version FROM routing_orders WHERE order_id = '{orderId}'", Token));
        Assert.Matches(ConfigVersionPattern(), version);
    }

    [Fact]
    public async Task Post_TiedCaneSuppliers_FixedRandomSourceDecidesTheWinner()
    {
        // SUP-T003 and SUP-T004 tie on every key, so the pick is the random source's index into the tied pair.
        var winners = new List<string>();

        foreach (var value in new[] { 0, 1 })
        {
            await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, default, randomValue: value);

            var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00003", false, (cane, 1)));
            var winner = Assert.Single(reply.SupplierIds);

            Assert.Contains(winner, new[] { "SUP-T003", "SUP-T004" });
            Assert.Equal(1, host.Random.CallCount);
            winners.Add(winner);
        }

        Assert.NotEqual(winners[0], winners[1]);
    }
}

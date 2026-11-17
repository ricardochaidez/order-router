using System.Text.RegularExpressions;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-024 harness checks: the pinned host really puts each experiment on the intended arm, stores a
/// <c>config_version</c>, and routes EXP-4 treatment tie-breaks through the fixed random source. Without these a config mistake
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

        await StoredArms.AssertAsync(fixture.ConnectionString, reply.Json, new ExperimentCombination(exp1, exp2, exp3), Token);
        Assert.True(number is >= 1 and <= 8);
        var version = Assert.Single(await fixture.QueryAsync($"SELECT config_version FROM routing_orders WHERE order_id = '{orderId}'", Token));
        Assert.Matches(ConfigVersionPattern(), version);
    }

    [Fact]
    public async Task Post_TiedCaneSuppliersUnderExp4Treatment_FixedRandomSourceDecidesTheWinner()
    {
        // SUP-T003 and SUP-T004 tie on every key, so under the EXP-4 treatment the pick is the random source's index into the tied pair.
        var winners = new List<string>();

        foreach (var value in new[] { 0, 1 })
        {
            await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(false, false, false, true), randomValue: value);

            var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00003", false, (cane, 1)));
            var winner = Assert.Single(reply.SupplierIds);

            Assert.Contains(winner, new[] { "SUP-T003", "SUP-T004" });
            Assert.Equal(1, host.Random.CallCount);
            winners.Add(winner);
        }

        Assert.NotEqual(winners[0], winners[1]);
    }

    [Theory]
    [InlineData(false, "SUP-T003", 0)]
    [InlineData(true, "SUP-T004", 1)]
    public async Task Post_TiedCaneSuppliers_Exp4ArmDecidesWhetherTheRandomSourceIsUsed(bool exp4, string expectedWinner, int randomCalls)
    {
        // The fixed source returns index 1, so the treatment picks SUP-T004; the control (core) ignores it and picks the lowest ID.
        var combination = new ExperimentCombination(false, false, false, exp4);
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, combination, randomValue: 1);

        var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "00003", false, (cane, 1)));

        Assert.Equal([expectedWinner], reply.SupplierIds);
        Assert.Equal(randomCalls, host.Random.CallCount);
        await StoredArms.AssertAsync(fixture.ConnectionString, reply.Json, combination, Token);
    }
}

using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// The response never tells the caller which experiment arm an order got or how the plan was searched: that evidence
/// is for analysis only and lives in the stored record and the decision log. These tests pin that no
/// <c>diagnostics</c> property appears on POST, the alias or GET, with every experiment on treatment.
/// </summary>
public class NoDiagnosticsApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/route")]
    [InlineData("/api/route")]
    public async Task Post_ExperimentsOnTreatment_HasNoDiagnosticsProperty(string path)
    {
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(true, true, true));

        var reply = await AcceptanceApi.PostCreatedAsync(host.Factory, path, AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "10015", false, ("WC-STD-001", 1)));

        Assert.False(reply.Json.TryGetProperty("diagnostics", out _));
        Assert.DoesNotContain("experiment_arms", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("search_mode", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ExperimentsOnTreatment_HasNoDiagnosticsPropertyButTheArmsAreStored()
    {
        await using var host = ExperimentArmsHost.Create(fixture.ConnectionString, new ExperimentCombination(true, true, true));
        var post = await AcceptanceApi.PostCreatedAsync(host.Factory, "/api/v1/route", AcceptanceApi.Body(AcceptanceApi.NewOrderId(), "10015", false, ("WC-STD-001", 1)));

        var get = await AcceptanceApi.GetAsync(host.Factory, post.Location!);

        Assert.False(get.Json.TryGetProperty("diagnostics", out _));
        Assert.DoesNotContain("experiment_arms", get.Text, StringComparison.Ordinal);
        await StoredArms.AssertAsync(fixture.ConnectionString, post.Json, new ExperimentCombination(true, true, true), Token);
    }
}

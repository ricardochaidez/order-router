using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Sections;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>AC4 (OR-012): the limits come from the <c>PlanSearch</c> configuration section of the real host.</summary>
public class PlanSearchConfigurationTests(ReferenceDataFixture fixture)
{
    private static PlanSearchRequest ThreeCategoryOrder() => new(
        new[] { "a", "b", "c" }.Select(name => new CategoryDemand(new Category(name), 1)),
        [new SupplierCandidate("S1", new[] { "a", "b", "c" }.Select(name => new Category(name)), FulfillmentMode.Local, 8m)]);

    [Fact]
    public async Task Host_NoOverride_UsesAppsettingsDefaults()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<PlanSearchOptions>>().Value;

        Assert.Equal(8, options.MaxExactCategories);
        Assert.Equal(200_000, options.NodeBudget);
        Assert.Equal(SearchMode.Exact, factory.Services.GetRequiredService<IPlanSearchService>().Search(ThreeCategoryOrder()).Mode);
    }

    [Fact]
    public async Task Host_OverriddenSettings_AreUsedByTheResolvedService()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PlanSearch:MaxExactCategories", "2");
            builder.UseSetting("PlanSearch:NodeBudget", "5");
        });
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<PlanSearchOptions>>().Value;
        var result = factory.Services.GetRequiredService<IPlanSearchService>().Search(ThreeCategoryOrder());

        Assert.Equal(2, options.MaxExactCategories);
        Assert.Equal(5, options.NodeBudget);
        Assert.Equal(SearchMode.Greedy, result.Mode);
        Assert.Equal(SearchFallbackReason.TooManyCategories, result.FallbackReason);
    }

    [Fact]
    public async Task Host_InvalidNodeBudget_FailsToStart()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.UseSetting("PlanSearch:NodeBudget", "0"));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("node budget", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

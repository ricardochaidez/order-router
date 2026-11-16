using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.PlanSearch;

namespace OrderRouter.UnitTests.Application.Routing.PlanSearch;

public class PlanSearchOptionsValidatorTests
{
    private readonly PlanSearchOptionsValidator validator = new();

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        Assert.True(validator.Validate(null, new PlanSearchOptions()).Succeeded);
    }

    [Fact]
    public void Validate_BadCategoryLimit_Fails()
    {
        var result = validator.Validate(null, new PlanSearchOptions { MaxExactCategories = 33 });

        Assert.True(result.Failed);
        Assert.Contains("max exact categories", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validate_BadNodeBudget_Fails()
    {
        var result = validator.Validate(null, new PlanSearchOptions { NodeBudget = 0 });

        Assert.Contains("node budget", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsAllAtOnce()
    {
        var result = validator.Validate(null, new PlanSearchOptions { MaxExactCategories = 0, NodeBudget = -1 });

        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Options_Defaults_MatchAdr()
    {
        var limits = new PlanSearchOptions().ToLimits();

        Assert.Equal(8, limits.MaxExactCategories);
        Assert.Equal(200_000, limits.NodeBudget);
    }
}

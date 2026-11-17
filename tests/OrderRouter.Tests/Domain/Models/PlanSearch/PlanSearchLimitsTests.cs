using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Models.PlanSearch;

public class PlanSearchLimitsTests
{
    [Fact]
    public void Default_UsesAdrValues()
    {
        Assert.Equal(8, PlanSearchLimits.Default.MaxExactCategories);
        Assert.Equal(200_000, PlanSearchLimits.Default.NodeBudget);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(32, int.MaxValue)]
    public void Constructor_BoundaryValues_AreAccepted(int categories, int budget)
    {
        var limits = new PlanSearchLimits(categories, budget);

        Assert.Equal(categories, limits.MaxExactCategories);
        Assert.Equal(budget, limits.NodeBudget);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(33, 10)]
    [InlineData(-1, 10)]
    [InlineData(8, 0)]
    [InlineData(8, -5)]
    public void Constructor_OutOfRange_Throws(int categories, int budget)
    {
        Assert.Throws<DomainValidationException>(() => new PlanSearchLimits(categories, budget));
    }

    [Fact]
    public void Constructor_SeveralProblems_ReportsAllAtOnce()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new PlanSearchLimits(0, 0));

        Assert.Contains("max exact categories", exception.Message);
        Assert.Contains("node budget", exception.Message);
    }

    [Fact]
    public void Validate_ValidValues_ReturnsNoErrors()
    {
        Assert.Empty(PlanSearchLimits.Validate(8, 200_000));
    }
}

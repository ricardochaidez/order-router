using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;
using static OrderRouter.Tests.TestData.TieBreakTestData;

namespace OrderRouter.Tests.Domain.Services;

public class DeterministicTieBreakerTests
{
    [Fact]
    public void Pick_TiedPlans_ChoosesTheLowestSupplierIdInOrdinalOrder()
    {
        var plans = new[] { Plan("SUP-B"), Plan("SUP-A"), Plan("SUP-C") };

        var result = DeterministicTieBreaker.Instance.Pick(plans);

        Assert.Same(plans[1], result.Chosen);
        Assert.Equal(plans, result.TiedCandidates);
        Assert.True(result.UsedRandom);
    }

    [Fact]
    public void Pick_SameTiedPlansInAnyOrder_ChoosesTheSamePlan()
    {
        var a = Plan("SUP-A");
        var b = Plan("SUP-B");

        Assert.Same(a, DeterministicTieBreaker.Instance.Pick([a, b]).Chosen);
        Assert.Same(a, DeterministicTieBreaker.Instance.Pick([b, a]).Chosen);
    }

    [Fact]
    public void Pick_OrdinalOrder_UppercaseBeforeLowercase()
    {
        var plans = new[] { Plan("sup-a"), Plan("SUP-Z") };

        Assert.Same(plans[1], DeterministicTieBreaker.Instance.Pick(plans).Chosen);
    }

    [Fact]
    public void Pick_SinglePlan_ReturnsIt()
    {
        var plans = Plans(1);

        var result = DeterministicTieBreaker.Instance.Pick(plans);

        Assert.Same(plans[0], result.Chosen);
        Assert.False(result.UsedRandom);
    }

    [Fact]
    public void Pick_Empty_Throws()
        => Assert.Throws<ArgumentException>(() => DeterministicTieBreaker.Instance.Pick([]));

    [Fact]
    public void Pick_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => DeterministicTieBreaker.Instance.Pick(null!));
}

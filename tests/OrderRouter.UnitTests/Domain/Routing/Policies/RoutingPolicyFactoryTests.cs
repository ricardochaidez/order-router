using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class RoutingPolicyFactoryTests
{
    private readonly RoutingPolicyFactory factory = new();

    [Fact]
    public void Create_AllControl_BuildsTheControlPolicies()
    {
        // AC8
        var policy = factory.Create(ExperimentAssignment.AllControl);

        Assert.IsType<UnratedLastPolicy>(policy.Rating);
        Assert.IsType<StrictPriorityPolicy>(policy.Selection);
        Assert.IsType<AllOrNothingPolicy>(policy.Unfulfillable);
        Assert.Same(ExperimentAssignment.AllControl, policy.Assignment);
    }

    [Fact]
    public void Create_CustomBand_ReachesSelectionPolicy()
    {
        var plans = new PlanSearcher().Search(new PlanSearchRequest(
            [Demand("cane")],
            [Mail("MAIL", 9.0m, "cane"), Local("LOCAL", 8.7m, "cane")])).Plans;

        var wide = factory.Create(Assignment(0.5m)).Selection.SelectBest(plans);
        var narrow = factory.Create(Assignment(0.1m)).Selection.SelectBest(plans);

        Assert.Equal(["LOCAL"], Ids(Assert.Single(wide)));
        Assert.Equal(["MAIL"], Ids(Assert.Single(narrow)));
    }

    [Theory]
    [InlineData(ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control, "EXP-1")]
    [InlineData(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control, "EXP-2")]
    [InlineData(ExperimentArm.Control, ExperimentArm.Control, ExperimentArm.Treatment, "EXP-3")]
    public void Create_TreatmentArm_ThrowsNamingTheExperiment(ExperimentArm exp1, ExperimentArm exp2, ExperimentArm exp3, string name)
    {
        var assignment = new ExperimentAssignment(new ExperimentArms(exp1, exp2, exp3), RoutingParameters.Default);

        var exception = Assert.Throws<NotSupportedException>(() => factory.Create(assignment));

        Assert.Contains(name, exception.Message);
    }

    [Fact]
    public void Create_UndefinedArm_Throws()
    {
        var assignment = new ExperimentAssignment(
            new ExperimentArms((ExperimentArm)99, ExperimentArm.Control, ExperimentArm.Control), RoutingParameters.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(assignment));
    }

    [Fact]
    public void Create_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => factory.Create(null!));

    [Fact]
    public void Create_TwoCalls_GiveIndependentSelectionPolicies()
    {
        var first = factory.Create(Assignment(0.5m));
        var second = factory.Create(Assignment(0.1m));

        Assert.NotSame(first.Selection, second.Selection);
    }

    private static ExperimentAssignment Assignment(decimal band)
        => new(ExperimentArms.AllControl, new RoutingParameters(similarityBand: band));
}

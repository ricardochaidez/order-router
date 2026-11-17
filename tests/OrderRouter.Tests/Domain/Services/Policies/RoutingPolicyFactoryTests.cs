using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Services.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using static OrderRouter.Tests.TestData.PlanSearchTestData;

namespace OrderRouter.Tests.Domain.Services.Policies;

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

    [Fact]
    public void Create_Exp1Treatment_BuildsBoundedTradeOffAndLeavesTheOtherArmsOnControl()
    {
        var policy = factory.Create(Exp1Treatment(RoutingParameters.Default));

        var selection = Assert.IsType<BoundedTradeOffPolicy>(policy.Selection);
        Assert.Equal(1, selection.MaxExtraShipments);
        Assert.IsType<UnratedLastPolicy>(policy.Rating);
        Assert.IsType<AllOrNothingPolicy>(policy.Unfulfillable);
    }

    [Fact]
    public void Create_Exp1TreatmentWithDifferentThresholds_GivesIndependentPoliciesThatFlipTheSamePlans()
    {
        // AC4
        var plans = new PlanSearcher().Search(new PlanSearchRequest(
            [Demand("cane"), Demand("walker")],
            [
                Local("ONE", 6m, "cane", "walker"),
                Local("TWO-A", 9m, "cane"),
                Local("TWO-B", 9m, "walker"),
            ],
            maxExtraShipments: 1)).Plans;

        var low = factory.Create(Exp1Treatment(new RoutingParameters(qualityUpliftThreshold: 2m)));
        var high = factory.Create(Exp1Treatment(new RoutingParameters(qualityUpliftThreshold: 4m)));

        Assert.NotSame(low.Selection, high.Selection);
        Assert.Equal(2, low.Selection.SelectBest(plans).Single().ShipmentCount);
        Assert.Equal(1, high.Selection.SelectBest(plans).Single().ShipmentCount);
    }

    [Fact]
    public void Create_Exp1Treatment_BandReachesThePolicy()
    {
        var plans = new PlanSearcher().Search(new PlanSearchRequest(
            [Demand("cane")],
            [Mail("MAIL", 9.0m, "cane"), Local("LOCAL", 8.7m, "cane")])).Plans;

        var wide = factory.Create(Exp1Treatment(new RoutingParameters(similarityBand: 0.5m))).Selection.SelectBest(plans);
        var narrow = factory.Create(Exp1Treatment(new RoutingParameters(similarityBand: 0.1m))).Selection.SelectBest(plans);

        Assert.Equal(["LOCAL"], Ids(Assert.Single(wide)));
        Assert.Equal(["MAIL"], Ids(Assert.Single(narrow)));
    }

    [Fact]
    public void Create_AllTreatment_DoesNotThrow()
    {
        var policy = factory.Create(new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Treatment, ExperimentArm.Treatment, ExperimentArm.Treatment), RoutingParameters.Default));

        Assert.IsType<BoundedTradeOffPolicy>(policy.Selection);
        Assert.IsType<ColdStartPriorPolicy>(policy.Rating);
        Assert.IsType<PartialRoutingPolicy>(policy.Unfulfillable);
    }

    [Fact]
    public void Create_Exp3Treatment_BuildsPartialRoutingAndLeavesTheOtherArmsOnControl()
    {
        var policy = factory.Create(Exp3(ExperimentArm.Treatment));

        Assert.IsType<PartialRoutingPolicy>(policy.Unfulfillable);
        Assert.IsType<UnratedLastPolicy>(policy.Rating);
        Assert.IsType<StrictPriorityPolicy>(policy.Selection);
    }

    [Fact]
    public void Create_Exp3Control_BuildsAllOrNothing()
        => Assert.IsType<AllOrNothingPolicy>(factory.Create(Exp3(ExperimentArm.Control)).Unfulfillable);

    [Fact]
    public void Create_Exp2Treatment_BuildsTheColdStartPolicyAndLeavesTheOtherArmsOnControl()
    {
        var policy = factory.Create(Exp2Treatment(RoutingParameters.Default));

        Assert.IsType<ColdStartPriorPolicy>(policy.Rating);
        Assert.IsType<StrictPriorityPolicy>(policy.Selection);
        Assert.IsType<AllOrNothingPolicy>(policy.Unfulfillable);
    }

    [Fact]
    public void Create_Exp2Control_BuildsUnratedLast()
        => Assert.IsType<UnratedLastPolicy>(factory.Create(ExperimentAssignment.AllControl).Rating);

    [Fact]
    public void Create_Exp2TreatmentWithDifferentPriors_GivesPoliciesThatRateDifferently()
    {
        // AC4
        var unrated = new Supplier("S", "S", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-19999")]), null, false);

        var low = factory.Create(Exp2Treatment(new RoutingParameters(unratedPrior: 4m)));
        var high = factory.Create(Exp2Treatment(new RoutingParameters(unratedPrior: 8m)));

        Assert.Equal(4m, low.Rating.EffectiveRating(unrated));
        Assert.Equal(8m, high.Rating.EffectiveRating(unrated));
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

    private static ExperimentAssignment Exp1Treatment(RoutingParameters parameters)
        => new(new ExperimentArms(ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control), parameters);

    private static ExperimentAssignment Exp3(ExperimentArm arm)
        => new(new ExperimentArms(ExperimentArm.Control, ExperimentArm.Control, arm), RoutingParameters.Default);

    private static ExperimentAssignment Exp2Treatment(RoutingParameters parameters)
        => new(new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control), parameters);

    private static ExperimentAssignment Assignment(decimal band)
        => new(ExperimentArms.AllControl, new RoutingParameters(similarityBand: band));
}

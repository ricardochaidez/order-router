using Microsoft.Extensions.Logging.Abstractions;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Sections;
using OrderRouter.Domain.Services.Policies;
using OrderRouter.Domain.Services;
using OrderRouter.Infrastructure.Services;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Domain.Services;

/// <summary>EXP-2 acceptance tests through the real search, selection and factory (no fakes).</summary>
public class ColdStartRoutingTests
{
    private static readonly ZipCode zip = ZipCode.Parse("10015");
    private static readonly RoutingPolicy control = new RoutingPolicyFactory().Create(ExperimentAssignment.AllControl);

    // Both suppliers use the same fulfillment mode (local): the similarity band only prefers local over mail
    // among plans of equal shipment count, so a mixed pair would hide the effect of the prior.
    private static Supplier Supplier(string id, decimal? rating)
        => new(id, id, [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), rating, false);

    private static OrderToRoute Order() => new(zip, false, [new OrderLine("A", 1, new Category("cane"))]);

    private static OrderRoutingService Service()
        => new(new SupplierEligibility(ZipRange.Parse("00100-99999")), new CountingPlanSearch(), new UniformRandomTieBreaker(new FixedRandomSource(0)));

    private static RoutingPolicy Treatment(decimal prior)
        => new RoutingPolicyFactory().Create(new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control),
            new RoutingParameters(unratedPrior: prior)));

    private static string Winner(RoutingDecision decision) => Assert.Single(decision.RoutedLines.Select(l => l.Supplier.Id).Distinct());

    [Fact]
    public void Route_TreatmentUnratedVersusSixPointZero_ChoosesTheUnrated()
    {
        // AC2
        var decision = Service().Route(Order(), [Supplier("RATED", 6.0m), Supplier("UNRATED", null)], Treatment(6.5m));

        Assert.Equal("UNRATED", Winner(decision));
    }

    [Fact]
    public void Route_ControlUnratedVersusSixPointZero_ChoosesTheRated()
    {
        // AC3
        var decision = Service().Route(Order(), [Supplier("RATED", 6.0m), Supplier("UNRATED", null)], control);

        Assert.Equal("RATED", Winner(decision));
    }

    [Fact]
    public void Route_TreatmentSevenPointZeroVersusUnrated_StillChoosesTheRated()
    {
        var decision = Service().Route(Order(), [Supplier("RATED", 7.0m), Supplier("UNRATED", null)], Treatment(6.5m));

        Assert.Equal("RATED", Winner(decision));
    }

    [Fact]
    public void Route_ControlOnlyUnratedSupplier_IsStillEligibleAndRouted()
    {
        // AB-03
        var decision = Service().Route(Order(), [Supplier("UNRATED", null)], control);

        Assert.Equal(RoutingOutcomeStatus.Routed, decision.Status);
        Assert.Equal("UNRATED", Winner(decision));
    }

    [Fact]
    public void Route_PriorChangedInConfiguration_NextAssignmentChangesTheWinner()
    {
        // AC4
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(Enabled(6.5m));
        using var assigner = new ConfigurationExperimentAssigner(monitor, NullLogger<ConfigurationExperimentAssigner>.Instance);
        var context = new AssignmentContext("caller", zip, "ORD-1");
        var suppliers = new[] { Supplier("RATED", 6.0m), Supplier("UNRATED", null) };
        var factory = new RoutingPolicyFactory();

        var before = Service().Route(Order(), suppliers, factory.Create(assigner.Assign(context)));

        monitor.Change(Enabled(4.0m));

        var after = Service().Route(Order(), suppliers, factory.Create(assigner.Assign(context)));

        Assert.Equal("UNRATED", Winner(before));
        Assert.Equal("RATED", Winner(after));
    }

    private static ExperimentsOptions Enabled(decimal prior)
    {
        var exp2 = new ExperimentOptions { Enabled = true, TreatmentPercentage = 100, Salt = "exp2" };
        exp2.Parameters.UnratedPrior = prior;

        return new ExperimentsOptions { Experiments = new ExperimentsSection { Exp2UnratedSuppliers = exp2 } };
    }
}

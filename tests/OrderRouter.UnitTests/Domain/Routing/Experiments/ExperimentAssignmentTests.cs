using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.UnitTests.Domain.Routing.Experiments;

public class ExperimentAssignmentTests
{
    [Fact]
    public void AllControl_IsEveryArmControlWithDefaultParameters()
    {
        var assignment = ExperimentAssignment.AllControl;

        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp1ConsolidationVsQuality);
        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp3PartialRouting);
        Assert.Equal(RoutingParameters.Default, assignment.Parameters);
    }

    [Fact]
    public void Constructor_NullArms_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentAssignment(null!, RoutingParameters.Default));

    [Fact]
    public void Constructor_NullParameters_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentAssignment(ExperimentArms.AllControl, null!));
}

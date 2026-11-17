using OrderRouter.Domain.Entities.Experiments;

namespace OrderRouter.Tests.Domain.Entities.Experiments;

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
        Assert.Equal(ExperimentReasons.AllDisabled, assignment.Reasons);
        Assert.Equal(ExperimentAssignment.DefaultConfigVersion, assignment.ConfigVersion);
    }

    [Fact]
    public void Constructor_WithReasonsAndVersion_KeepsThem()
    {
        var reasons = new ExperimentReasons(ExposureReason.Bucketed, ExposureReason.NotTargeted, ExposureReason.Disabled, ExposureReason.Disabled);

        var assignment = new ExperimentAssignment(ExperimentArms.AllControl, RoutingParameters.Default, reasons, "abc123");

        Assert.Equal(reasons, assignment.Reasons);
        Assert.Equal("abc123", assignment.ConfigVersion);
    }

    [Fact]
    public void Constructor_NullArms_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentAssignment(null!, RoutingParameters.Default));

    [Fact]
    public void Constructor_NullParameters_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentAssignment(ExperimentArms.AllControl, null!));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankVersion_Throws(string version)
        => Assert.Throws<ArgumentException>(() => new ExperimentAssignment(ExperimentArms.AllControl, RoutingParameters.Default, null, version));

    [Fact]
    public void Constructor_NullVersion_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentAssignment(ExperimentArms.AllControl, RoutingParameters.Default, null, null!));
}

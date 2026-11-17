using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;

namespace OrderRouter.Tests.Domain.Entities.Experiments;

public class ExperimentConfigurationTests
{
    private static readonly ZipCode anyZip = ZipCode.Parse("10015");

    private static ExperimentDefinition Exp(string id, bool enabled = true, int percentage = 100, ExperimentTargeting? targeting = null)
        => new(id, enabled, percentage, id.ToLowerInvariant(), targeting ?? ExperimentTargeting.Everyone);

    private static ExperimentConfiguration Config(
        ExperimentDefinition? exp1 = null,
        ExperimentDefinition? exp2 = null,
        ExperimentDefinition? exp3 = null,
        ExperimentDefinition? exp4 = null,
        string version = "v1")
        => new(
            exp1 ?? ExperimentDefinition.Disabled(nameof(ExperimentArms.Exp1ConsolidationVsQuality), "exp1"),
            exp2 ?? ExperimentDefinition.Disabled(nameof(ExperimentArms.Exp2UnratedSuppliers), "exp2"),
            exp3 ?? ExperimentDefinition.Disabled(nameof(ExperimentArms.Exp3PartialRouting), "exp3"),
            exp4 ?? ExperimentDefinition.Disabled(nameof(ExperimentArms.Exp4TieBreaking), "exp4"),
            RoutingParameters.Default,
            version);

    private static ExperimentDefinition Exp2(int percentage, ExperimentTargeting? targeting = null)
        => Exp(nameof(ExperimentArms.Exp2UnratedSuppliers), true, percentage, targeting);

    private static ExperimentTargeting Targeting(string[]? callers = null, string[]? prefixes = null, string[]? ranges = null)
        => new(callers ?? [], prefixes ?? [], [.. (ranges ?? []).Select(ZipRange.Parse)]);

    [Fact]
    public void Assign_Exp4Enabled_OnlyExp4GetsTreatment()
    {
        var config = Config(exp4: Exp(nameof(ExperimentArms.Exp4TieBreaking)));

        var assignment = config.Assign("c", ZipCode.Parse("30500"), "ORD-1");

        Assert.Equal(ExperimentArm.Treatment, assignment.Arms.Exp4TieBreaking);
        Assert.Equal(ExposureReason.Bucketed, assignment.Reasons.Exp4TieBreaking);
        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp1ConsolidationVsQuality);
        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp3PartialRouting);
    }

    [Fact]
    public void Assign_AllDisabled_IsControlDisabledWithTheVersion()
    {
        // AC1
        var assignment = Config(version: "abc").Assign("caller", anyZip, "ORD-1");

        Assert.Equal(ExperimentArms.AllControl, assignment.Arms);
        Assert.Equal(ExperimentReasons.AllDisabled, assignment.Reasons);
        Assert.Equal("abc", assignment.ConfigVersion);
        Assert.Equal(RoutingParameters.Default, assignment.Parameters);
    }

    [Fact]
    public void Assign_Disabled_IgnoresTargetingAndPercentage()
    {
        var disabled = Exp(nameof(ExperimentArms.Exp2UnratedSuppliers), enabled: false, percentage: 100, targeting: Targeting(prefixes: ["021"]));

        var assignment = Config(exp2: disabled).Assign("caller", anyZip, "ORD-1");

        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Disabled, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_EnabledAtZeroPercent_IsControlBucketed()
    {
        var assignment = Config(exp2: Exp2(0)).Assign("caller", anyZip, "ORD-1");

        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_EnabledAtHundredPercent_IsTreatmentBucketed()
    {
        var assignment = Config(exp2: Exp2(100)).Assign("caller", anyZip, "ORD-1");

        Assert.Equal(ExperimentArm.Treatment, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_ZipOutsideThePrefix_IsControlNotTargeted()
    {
        // AC2: prefix 021 does not match 10015, even at 100%.
        var assignment = Config(exp2: Exp2(100, Targeting(prefixes: ["021"]))).Assign("caller", ZipCode.Parse("10015"), "ORD-1");

        Assert.Equal(ExperimentArm.Control, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.NotTargeted, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_ZipInsideThePrefix_IsTreatment()
    {
        var assignment = Config(exp2: Exp2(100, Targeting(prefixes: ["021"]))).Assign("caller", ZipCode.Parse("02114"), "ORD-1");

        Assert.Equal(ExperimentArm.Treatment, assignment.Arms.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_SameCaller_SameArmForTwentyOrders()
    {
        // AC3
        var config = Config(exp2: Exp2(50));

        var arms = Enumerable.Range(0, 20)
            .Select(i => config.Assign("one-caller", anyZip, $"ORD-{i}").Arms.Exp2UnratedSuppliers)
            .Distinct()
            .ToList();

        Assert.Single(arms);
    }

    [Fact]
    public void Assign_RampUp_OnlyAddsTreatmentCallers()
    {
        // AC5: the treatment set at 20% is a subset of the set at 50%.
        var callers = Enumerable.Range(0, 1_000).Select(i => $"caller-{i}").ToList();

        HashSet<string> TreatmentAt(int percentage)
        {
            var config = Config(exp2: Exp2(percentage));

            return [.. callers.Where(c => config.Assign(c, anyZip, "ORD").Arms.Exp2UnratedSuppliers == ExperimentArm.Treatment)];
        }

        var at20 = TreatmentAt(20);
        var at50 = TreatmentAt(50);

        Assert.NotEmpty(at20);
        Assert.True(at20.IsProperSubsetOf(at50));
    }

    [Fact]
    public void Assign_TwoExperimentsAtFiftyPercent_AreIndependent()
    {
        // AC8: with independent salts each cell of the 2x2 table holds about 25% of 10,000 callers.
        // The hash is deterministic, so this does not flake; the tolerance (22.5%-27.5%) is about 5 sigma.
        var config = Config(
            exp1: Exp(nameof(ExperimentArms.Exp1ConsolidationVsQuality), percentage: 50),
            exp2: Exp2(50));

        var cells = Enumerable.Range(0, 10_000)
            .Select(i => config.Assign($"caller-{i}", anyZip, "ORD").Arms)
            .GroupBy(arms => (arms.Exp1ConsolidationVsQuality, arms.Exp2UnratedSuppliers))
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(4, cells.Count);
        Assert.All(cells.Values, count => Assert.InRange(count, 2_250, 2_750));
    }

    [Fact]
    public void Assign_CallerTargeting_OnlyListedCallersAreTreated()
    {
        // AC9
        var config = Config(exp2: Exp2(100, Targeting(callers: ["portal-client"])));

        var listed = config.Assign("portal-client", anyZip, "ORD-1");
        var other = config.Assign("other-client", anyZip, "ORD-1");

        Assert.Equal(ExperimentArm.Treatment, listed.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, listed.Reasons.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Control, other.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.NotTargeted, other.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_CallerListAndNoCaller_IsNotTargeted()
    {
        var config = Config(exp2: Exp2(100, Targeting(callers: ["portal-client"])));

        var assignment = config.Assign(null, anyZip, "ORD-1");

        Assert.Equal(ExposureReason.NotTargeted, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_ZipRange_TargetsInsideAndNotOutside()
    {
        // AC10
        var config = Config(exp2: Exp2(100, Targeting(ranges: ["30000-30999"])));

        Assert.Equal(ExperimentArm.Treatment, config.Assign("c", ZipCode.Parse("30500"), "ORD-1").Arms.Exp2UnratedSuppliers);

        var outside = config.Assign("c", ZipCode.Parse("31000"), "ORD-1");

        Assert.Equal(ExperimentArm.Control, outside.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.NotTargeted, outside.Reasons.Exp2UnratedSuppliers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Assign_NoCaller_BucketsOnTheOrderId(string? caller)
    {
        // AC11: the bucket is the one of the order id, so the arm follows the order, not the (missing) caller.
        var config = Config(exp2: Exp2(50));

        for (var i = 0; i < 200; i++)
        {
            var orderId = $"ORD-{i}";
            var expected = ExperimentBucketer.Bucket(nameof(ExperimentArms.Exp2UnratedSuppliers), nameof(ExperimentArms.Exp2UnratedSuppliers).ToLowerInvariant(), orderId) < 50
                ? ExperimentArm.Treatment
                : ExperimentArm.Control;

            Assert.Equal(expected, config.Assign(caller, anyZip, orderId).Arms.Exp2UnratedSuppliers);
            Assert.Equal(expected, config.Assign(caller, anyZip, orderId).Arms.Exp2UnratedSuppliers);
        }
    }

    [Fact]
    public void Assign_NullOrderId_Throws()
        => Assert.Throws<ArgumentNullException>(() => Config().Assign("c", anyZip, null!));

    [Fact]
    public void Constructor_BlankVersion_Throws()
        => Assert.Throws<ArgumentException>(() => Config(version: " "));
}

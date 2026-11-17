using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Entities.Routing;

public class ItemDependencyRuleTests
{
    private static readonly Category bed = new("hospital bed");
    private static readonly Category lift = new("patient lift");
    private static readonly Category commode = new("commode");
    private static readonly ItemDependencyRule bedNeedsLift = new(bed, [lift]);

    private static RoutedLine Routed(string code, Category category)
        => new(code, 1, category, new Supplier("S1", "S1", [category], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false), FulfillmentMode.Local);

    private static UnfulfilledLine Missing(string code, Category? category)
        => new(code, 1, category, category is null ? UnfulfilledLine.UnknownProductCode : "NO_SUPPLIER_FOR_CATEGORY_IN_AREA");

    [Fact]
    public void Constructor_ValidRule_KeepsCategoryAndDistinctRequirementsInOrder()
    {
        var rule = new ItemDependencyRule(bed, [lift, commode, lift]);

        Assert.Equal(bed, rule.Category);
        Assert.Equal([lift, commode], rule.Requires);
        Assert.Equal([bed, lift, commode], rule.Categories);
    }

    [Fact]
    public void Constructor_NoRequirement_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new ItemDependencyRule(bed, []));

        Assert.Contains("at least one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RequiresItself_Throws()
    {
        // DR-01: a rule links two different categories.
        var exception = Assert.Throws<DomainValidationException>(() => new ItemDependencyRule(bed, [lift, new Category(" Hospital Bed ")]));

        Assert.Contains("cannot require itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DefaultCategories_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new ItemDependencyRule(default, [default]));

        Assert.Contains("category is missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("required category is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NullRequirements_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ItemDependencyRule(bed, null!));

    // OR-020 AC1, and the §9 example message.
    [Fact]
    public void WarningFor_RoutedItemWithUnfulfilledRequirement_NamesBothSides()
    {
        var warning = bedNeedsLift.WarningFor([Routed("HB-FUL-018", bed)], [Missing("PL-ELEC-043", lift)]);

        Assert.NotNull(warning);
        Assert.Equal("DEPENDENT_ITEM_UNFULFILLED", warning.Code);
        Assert.Equal(["PL-ELEC-043"], warning.UnfulfilledProductCodes);
        Assert.Equal(["HB-FUL-018"], warning.AffectedProductCodes);
        Assert.Equal(
            "HB-FUL-018 (hospital bed) is routed, but its required companion PL-ELEC-043 (patient lift) could not be fulfilled. Review before releasing.",
            warning.Message);
    }

    [Fact]
    public void WarningFor_SeveralLinesOnEachSide_ListsEachCodeOnceInRequestOrder()
    {
        var rule = new ItemDependencyRule(bed, [lift, commode]);

        var warning = rule.WarningFor(
            [Routed("HB-SEM-017", bed), Routed("HB-FUL-018", bed), Routed("HB-SEM-017", bed)],
            [Missing("PL-ELEC-043", lift), Missing("CM-BED-048", commode), Missing("PL-ELEC-043", lift)]);

        Assert.NotNull(warning);
        Assert.Equal(["PL-ELEC-043", "CM-BED-048"], warning.UnfulfilledProductCodes);
        Assert.Equal(["HB-SEM-017", "HB-FUL-018"], warning.AffectedProductCodes);
        Assert.StartsWith("HB-SEM-017 (hospital bed), HB-FUL-018 (hospital bed) are routed, but their required companions ", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningFor_RequirementRouted_ReturnsNull()
        => Assert.Null(bedNeedsLift.WarningFor([Routed("HB-FUL-018", bed), Routed("PL-ELEC-043", lift)], [Missing("CM-BED-048", commode)]));

    [Fact]
    public void WarningFor_DependentItemNotRouted_ReturnsNull()
        => Assert.Null(bedNeedsLift.WarningFor([Routed("CM-BED-048", commode)], [Missing("PL-ELEC-043", lift)]));

    // The rule is one-way: a routed lift does not need the bed.
    [Fact]
    public void WarningFor_ReverseDirection_ReturnsNull()
        => Assert.Null(bedNeedsLift.WarningFor([Routed("PL-ELEC-043", lift)], [Missing("HB-FUL-018", bed)]));

    // §9: an unknown product has no category, so it never matches a rule.
    [Fact]
    public void WarningFor_UnknownProduct_ReturnsNull()
        => Assert.Null(bedNeedsLift.WarningFor([Routed("HB-FUL-018", bed)], [Missing("PL-UNKNOWN-999", null)]));
}

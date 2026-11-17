using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Services;

namespace OrderRouter.Tests.Domain.Services;

public class SupplierEligibilityTests
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");
    private static readonly Category wheelchair = new("wheelchair");
    private static readonly ZipCode zip = ZipCode.Parse("00001");

    private static Supplier Build(string id, string[] ranges, bool canMailOrder = false, params string[] categories)
        => new(
            id,
            id,
            (categories.Length > 0 ? categories : ["wheelchair"]).Select(value => new Category(value)),
            new ServiceArea(ranges.Select(ZipRange.Parse)),
            null,
            canMailOrder);

    private static string[] Ids(EligibilityResult result) => [.. result.Suppliers.Select(entry => entry.Supplier.Id)];

    private static EligibilityResult Evaluate(bool mailOrder, ZipCode? customerZip = null, ZipRange? range = null, params Supplier[] suppliers)
        => new SupplierEligibility(range ?? nationwide).Evaluate(wheelchair, customerZip ?? zip, mailOrder, suppliers);

    [Fact]
    public void Evaluate_LocalOrder_OnlyHandlesServesAndNotMailOnly()
    {
        var local = Build("LOCAL", ["00001"]);
        var noCategory = Build("OTHER", ["00001"], false, "cpap");
        var doesNotServe = Build("FAR", ["10000-10010"]);
        var mailOnly = Build("MAIL", ["00100-99999"], true);

        var result = Evaluate(false, null, null, local, noCategory, doesNotServe, mailOnly);

        Assert.Equal(["LOCAL"], Ids(result));
        Assert.All(result.Suppliers, entry => Assert.Equal(FulfillmentMode.Local, entry.Mode));
    }

    [Fact]
    public void Evaluate_LocalOrder_ExcludesMailOnlySupplier()
    {
        var result = Evaluate(false, ZipCode.Parse("10015"), null, Build("MAIL", ["00100-99999"], true));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_NationwideWithoutMailOrder_IsEligibleLocalInsideRange()
    {
        var result = Evaluate(false, ZipCode.Parse("10015"), null, Build("NATION", ["00100-99999"], false));

        Assert.Equal(["NATION"], Ids(result));
        Assert.Equal(FulfillmentMode.Local, result.Suppliers[0].Mode);
    }

    [Fact]
    public void Evaluate_NationwideWithoutMailOrder_NotEligibleBelowRange()
    {
        var result = Evaluate(false, zip, null, Build("NATION", ["00100-99999"], false));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_MailOrder_OnlyMailCapableWhateverTheZip()
    {
        var farMail = Build("FAR-MAIL", ["10000-10010"], true);
        var servingNoMail = Build("NO-MAIL", ["00001"], false);

        var result = Evaluate(true, null, null, farMail, servingNoMail);

        Assert.Equal(["FAR-MAIL"], Ids(result));
    }

    [Fact]
    public void Evaluate_MailOrder_ModeFollowsFr09()
    {
        var mailOnly = Build("A-MAILONLY", ["00100-99999"], true);
        var serving = Build("B-SERVING", ["00001"], true);
        var notServing = Build("C-NOTSERVING", ["10000-10010"], true);

        var result = Evaluate(true, null, null, notServing, serving, mailOnly);

        Assert.Equal(
            [FulfillmentMode.Mail, FulfillmentMode.Local, FulfillmentMode.Mail],
            result.Suppliers.Select(entry => entry.Mode));
    }

    [Fact]
    public void Evaluate_MailOrderOnZipMailOnlyDoesNotCover_MailOnlyIsStillMail()
    {
        var result = Evaluate(true, ZipCode.Parse("00050"), null, Build("MAILONLY", ["00100-99999"], true));

        Assert.Equal(FulfillmentMode.Mail, result.Suppliers[0].Mode);
    }

    [Fact]
    public void Evaluate_LocalOrderNobodyEligible_ReturnsInAreaReason()
    {
        var result = Evaluate(false, null, null, Build("FAR", ["10000-10010"]));

        Assert.Equal(UnfulfillableReason.NoSupplierForCategoryInArea, result.Reason);
        Assert.Empty(result.Suppliers);
    }

    [Fact]
    public void Evaluate_MailOrderNoMailCapableSupplier_ReturnsNoSupplierForCategory()
    {
        var result = Evaluate(true, null, null, Build("LOCAL", ["00001"], false));

        Assert.Equal(UnfulfillableReason.NoSupplierForCategory, result.Reason);
    }

    [Fact]
    public void Evaluate_MailOrderCategoryNobodyHandles_ReturnsNoSupplierForCategory()
    {
        var result = new SupplierEligibility(nationwide)
            .Evaluate(new Category("oxygen"), zip, true, [Build("MAIL", ["00100-99999"], true)]);

        Assert.Equal(UnfulfillableReason.NoSupplierForCategory, result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_EmptySupplierList_ReturnsUnfulfillable(bool mailOrder)
    {
        var result = Evaluate(mailOrder);

        Assert.False(result.IsEligible);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Evaluate_CategoryCaseDiffers_StillMatches()
    {
        var result = new SupplierEligibility(nationwide)
            .Evaluate(new Category("WheelChair"), zip, false, [Build("LOCAL", ["00001"])]);

        Assert.Equal(["LOCAL"], Ids(result));
    }

    [Fact]
    public void Evaluate_CustomNationwideRange_ChangesWhoIsMailOnly()
    {
        var supplier = Build("S", ["00100-99999"], true);
        var custom = ZipRange.Parse("00500-99999");
        var customerZip = ZipCode.Parse("10015");

        Assert.False(Evaluate(false, customerZip, nationwide, supplier).IsEligible);

        var result = Evaluate(false, customerZip, custom, supplier);

        Assert.Equal(["S"], Ids(result));
        Assert.Equal(FulfillmentMode.Local, result.Suppliers[0].Mode);
    }

    [Theory]
    [InlineData("00100", true)]
    [InlineData("99999", true)]
    [InlineData("00099", false)]
    public void Evaluate_BoundaryZips_AreInclusive(string customerZip, bool eligible)
    {
        var result = Evaluate(false, ZipCode.Parse(customerZip), null, Build("N", ["00100-99999"], false));

        Assert.Equal(eligible, result.IsEligible);
    }

    [Fact]
    public void Evaluate_SupplierWithSeveralSegments_ServesWhenAnySegmentContainsZip()
    {
        var result = Evaluate(false, ZipCode.Parse("20000"), null, Build("MULTI", ["00001", "19990-20010"]));

        Assert.Equal(["MULTI"], Ids(result));
    }

    [Fact]
    public void Evaluate_InputInAnyOrder_ReturnsSameOrder()
    {
        var a = Build("SUP-A", ["00001"]);
        var b = Build("SUP-B", ["00001"]);
        var c = Build("SUP-C", ["00001"]);

        Assert.Equal(Ids(Evaluate(false, null, null, a, b, c)), Ids(Evaluate(false, null, null, c, a, b)));
        Assert.Equal(["SUP-A", "SUP-B", "SUP-C"], Ids(Evaluate(false, null, null, c, b, a)));
    }

    [Fact]
    public void Evaluate_NullSuppliers_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SupplierEligibility(nationwide).Evaluate(wheelchair, zip, false, null!));
    }

    [Fact]
    public void Evaluate_DefaultCategory_MatchesNothingAndIsUnfulfillable()
    {
        var result = new SupplierEligibility(nationwide).Evaluate(default, zip, false, [Build("S", ["00001"])]);

        Assert.Equal(UnfulfillableReason.NoSupplierForCategoryInArea, result.Reason);
    }
}

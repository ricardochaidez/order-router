using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Services;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>OR-010 rules on the shared seeded database (containment checks, the database may hold older rows).</summary>
public class SeededEligibilityTests(ReferenceDataFixture fixture)
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");
    private static readonly SupplierEligibility eligibility = new(nationwide);
    private static readonly Category wheelchair = new("wheelchair");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<EligibilityResult> EvaluateAsync(Category category, string zip, bool mailOrder, Func<Supplier, bool>? filter = null)
    {
        var options = new DbContextOptionsBuilder<OrderRouterDataContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var context = new OrderRouterDataContext(options);
        var suppliers = await new ReferenceDataReader(context).GetSuppliersHandlingAsync([category], Token);

        return eligibility.Evaluate(category, ZipCode.Parse(zip), mailOrder, suppliers.Where(filter ?? (_ => true)));
    }

    [Fact]
    public async Task Evaluate_LocalWheelchairAtSentinelZip_ContainsSentinelAsLocal()
    {
        var zip = ZipCode.Parse("00001");
        var result = await EvaluateAsync(wheelchair, "00001", false);

        var sentinel = Assert.Single(result.Suppliers, entry => entry.Supplier.Id == "SUP-T001");
        Assert.Equal(FulfillmentMode.Local, sentinel.Mode);
        Assert.All(
            result.Suppliers,
            entry =>
            {
                Assert.True(entry.Supplier.Handles(wheelchair));
                Assert.True(entry.Supplier.Serves(zip));
                Assert.False(entry.Supplier.IsMailOnly(nationwide));
            });
    }

    [Fact]
    public async Task Evaluate_LocalOrderWhereMailOnlySuppliersServe_ExcludesThem()
    {
        var cpap = new Category("cpap");
        var zip = ZipCode.Parse("10015");
        var all = await EvaluateAsync(cpap, "10015", true);

        Assert.Contains(all.Suppliers, entry => entry.Supplier.IsMailOnly(nationwide) && entry.Supplier.Serves(zip));

        var local = await EvaluateAsync(cpap, "10015", false);

        Assert.All(local.Suppliers, entry => Assert.False(entry.Supplier.IsMailOnly(nationwide)));
        Assert.All(local.Suppliers, entry => Assert.Equal(FulfillmentMode.Local, entry.Mode));
    }

    [Fact]
    public async Task Evaluate_MailOrderWheelchair_ExcludesSentinelAndKeepsOnlyMailCapable()
    {
        var result = await EvaluateAsync(wheelchair, "00001", true);

        Assert.True(result.IsEligible);
        Assert.DoesNotContain(result.Suppliers, entry => entry.Supplier.Id == "SUP-T001");
        Assert.All(result.Suppliers, entry => Assert.True(entry.Supplier.CanMailOrder));
    }

    [Fact]
    public async Task Evaluate_LocalOtherCategoryAtOnlySupplierZip_ReturnsInAreaReason()
    {
        var result = await EvaluateAsync(new Category("oxygen"), "00098", false);

        Assert.Equal(UnfulfillableReason.NoSupplierForCategoryInArea, result.Reason);
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY_IN_AREA", result.Reason!.Value.Code());
        Assert.Empty(result.Suppliers);
    }

    [Fact]
    public async Task Evaluate_MailOrderWhenOnlyNonMailSuppliersHandleIt_ReturnsNoSupplierForCategory()
    {
        // Every seeded category has mail-capable suppliers, so the gap is built from the seeded sentinels (SUP-T001 and SUP-T014 are can_mail_order = n).
        var result = await EvaluateAsync(wheelchair, "00001", true, supplier => supplier.Id is "SUP-T001" or "SUP-T014");

        Assert.Equal(UnfulfillableReason.NoSupplierForCategory, result.Reason);
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY", result.Reason!.Value.Code());
    }

    [Fact]
    public async Task Evaluate_MailOrderCategoryNoSeededSupplierHandles_ReturnsNoSupplierForCategory()
    {
        var result = await EvaluateAsync(new Category("category-not-in-seed"), "00001", true);

        Assert.Equal(UnfulfillableReason.NoSupplierForCategory, result.Reason);
    }
}

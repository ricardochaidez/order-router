using OrderRouter.Domain.Common;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.Eligibility;

public class EligibilityResultTests
{
    private static EligibleSupplier Entry(string id, FulfillmentMode mode = FulfillmentMode.Local)
        => new(
            new Supplier(id, "Acme", [new Category("cpap")], new ServiceArea([ZipRange.Parse("02114-02212")]), null, false),
            mode);

    [Fact]
    public void Eligible_WithSuppliers_HasNoReason()
    {
        var result = EligibilityResult.Eligible([Entry("S1")]);

        Assert.True(result.IsEligible);
        Assert.Null(result.Reason);
        Assert.Single(result.Suppliers);
    }

    [Fact]
    public void Unfulfillable_WithReason_HasEmptyListAndReason()
    {
        var result = EligibilityResult.Unfulfillable(UnfulfillableReason.NoSupplierForCategory);

        Assert.False(result.IsEligible);
        Assert.Equal(UnfulfillableReason.NoSupplierForCategory, result.Reason);
        Assert.NotNull(result.Suppliers);
        Assert.Empty(result.Suppliers);
    }

    [Fact]
    public void Eligible_EmptyCollection_Throws()
    {
        Assert.Throws<DomainValidationException>(() => EligibilityResult.Eligible([]));
    }

    [Fact]
    public void Eligible_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EligibilityResult.Eligible(null!));
    }

    [Fact]
    public void Unfulfillable_UndefinedReason_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EligibilityResult.Unfulfillable((UnfulfillableReason)99));
    }

    [Fact]
    public void Eligible_UnorderedInput_OrdersByIdOrdinal()
    {
        var result = EligibilityResult.Eligible([Entry("SUP-b"), Entry("SUP-B"), Entry("SUP-A")]);

        Assert.Equal(["SUP-A", "SUP-B", "SUP-b"], result.Suppliers.Select(entry => entry.Supplier.Id));
    }

    [Fact]
    public void Eligible_CallerChangesSourceAfterwards_ResultIsUnaffected()
    {
        var source = new List<EligibleSupplier> { Entry("S1") };
        var result = EligibilityResult.Eligible(source);

        source.Add(Entry("S2"));

        Assert.Single(result.Suppliers);
    }

    [Fact]
    public void Suppliers_IsReadOnly()
    {
        var result = EligibilityResult.Eligible([Entry("S1")]);

        Assert.True(((ICollection<EligibleSupplier>)result.Suppliers).IsReadOnly);
    }
}

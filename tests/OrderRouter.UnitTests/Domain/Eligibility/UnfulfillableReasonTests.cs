using OrderRouter.Domain.Eligibility;

namespace OrderRouter.UnitTests.Domain.Eligibility;

public class UnfulfillableReasonTests
{
    [Fact]
    public void Code_NoSupplierForCategoryInArea_ReturnsExactString()
    {
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY_IN_AREA", UnfulfillableReason.NoSupplierForCategoryInArea.Code());
    }

    [Fact]
    public void Code_NoSupplierForCategory_ReturnsExactString()
    {
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY", UnfulfillableReason.NoSupplierForCategory.Code());
    }

    [Fact]
    public void Code_UndefinedValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((UnfulfillableReason)99).Code());
    }
}

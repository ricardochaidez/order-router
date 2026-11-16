using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class UnratedLastPolicyTests
{
    private static Supplier SupplierWith(decimal? rating)
        => new("SUP-1", "Test", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-19999")]), rating, false);

    [Fact]
    public void EffectiveRating_Unrated_IsZero()
        => Assert.Equal(0m, UnratedLastPolicy.Instance.EffectiveRating(SupplierWith(null)));

    [Theory]
    [InlineData(6.0)]
    [InlineData(10)]
    [InlineData(9.25)]
    public void EffectiveRating_Rated_IsTheRating(double rating)
        => Assert.Equal((decimal)rating, UnratedLastPolicy.Instance.EffectiveRating(SupplierWith((decimal)rating)));

    [Fact]
    public void EffectiveRating_NullSupplier_Throws()
        => Assert.Throws<ArgumentNullException>(() => UnratedLastPolicy.Instance.EffectiveRating(null!));
}

using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class ColdStartPriorPolicyTests
{
    private static Supplier SupplierWith(decimal? rating)
        => new("SUP-1", "Test", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-19999")]), rating, false);

    [Fact]
    public void EffectiveRating_UnratedWithDefaultPrior_Is6Point5()
        => Assert.Equal(6.5m, new ColdStartPriorPolicy(RoutingParameters.DefaultUnratedPrior).EffectiveRating(SupplierWith(null)));

    [Theory]
    [InlineData(8.0)]
    [InlineData(1)]
    [InlineData(10)]
    public void EffectiveRating_UnratedWithCustomPrior_IsThatPrior(double prior)
        => Assert.Equal((decimal)prior, new ColdStartPriorPolicy((decimal)prior).EffectiveRating(SupplierWith(null)));

    [Theory]
    [InlineData(6.0, 6.5)]
    [InlineData(10, 2.0)]
    [InlineData(9.25, 9.5)]
    public void EffectiveRating_Rated_IsTheRatingWhateverThePrior(double rating, double prior)
        => Assert.Equal((decimal)rating, new ColdStartPriorPolicy((decimal)prior).EffectiveRating(SupplierWith((decimal)rating)));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Constructor_PriorOnTheBoundary_IsAccepted(double prior)
        => Assert.NotNull(new ColdStartPriorPolicy((decimal)prior));

    [Theory]
    [InlineData(0.99)]
    [InlineData(10.01)]
    public void Constructor_PriorOutOfRange_Throws(double prior)
        => Assert.Throws<DomainValidationException>(() => new ColdStartPriorPolicy((decimal)prior));

    [Fact]
    public void EffectiveRating_NullSupplier_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ColdStartPriorPolicy(6.5m).EffectiveRating(null!));
}

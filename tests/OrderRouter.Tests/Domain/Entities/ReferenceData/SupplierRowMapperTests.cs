using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;
using OrderRouter.Infrastructure.Mappers;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Tests.Domain.Entities.ReferenceData;

public class SupplierRowMapperTests
{
    private static SupplierEntity Row(
        string[]? categories = null,
        (int Start, int End)[]? areas = null,
        decimal? rating = 7.5m,
        bool canMailOrder = true)
        => new()
        {
            SupplierId = "SUP-1",
            Name = "Acme DME",
            Rating = rating,
            CanMailOrder = canMailOrder,
            Categories = [.. (categories ?? ["cpap", "oxygen"]).Select(category => new SupplierCategoryEntity { SupplierId = "SUP-1", Category = category })],
            ServiceAreas = [.. (areas ?? [(2130, 2212), (90001, 90001)]).Select(area => new SupplierServiceAreaEntity { SupplierId = "SUP-1", StartZip = area.Start, EndZip = area.End })],
        };

    [Fact]
    public void ToDomain_FullRow_MapsEveryField()
    {
        var supplier = SupplierMappingExtensions.ToDomainModel(Row());

        Assert.Equal("SUP-1", supplier.Id);
        Assert.Equal("Acme DME", supplier.Name);
        Assert.Equal(7.5m, supplier.Rating?.Value);
        Assert.True(supplier.CanMailOrder);
        Assert.Equal([new Category("cpap"), new Category("oxygen")], supplier.Categories.OrderBy(category => category.Value, StringComparer.Ordinal));
        Assert.Equal(2, supplier.ServiceArea.Segments.Count);
    }

    [Fact]
    public void ToDomain_FourDigitStartZip_IsPaddedToFiveDigits()
    {
        var supplier = SupplierMappingExtensions.ToDomainModel(Row());

        Assert.Equal("02130", supplier.ServiceArea.Segments[0].Start.Value);
        Assert.True(supplier.Serves(ZipCode.Parse("02150")));
    }

    [Fact]
    public void ToDomain_NullRating_StaysUnrated()
    {
        var supplier = SupplierMappingExtensions.ToDomainModel(Row(rating: null));

        Assert.Null(supplier.Rating);
    }

    [Fact]
    public void ToDomain_CanMailOrderFalse_IsKept()
    {
        Assert.False(SupplierMappingExtensions.ToDomainModel(Row(canMailOrder: false)).CanMailOrder);
    }

    [Fact]
    public void ToDomain_NoServiceAreas_ThrowsNamingTheSupplier()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SupplierMappingExtensions.ToDomainModel(Row(areas: [])));

        Assert.Contains("SUP-1", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DomainValidationException>(exception.InnerException);
    }

    [Fact]
    public void ToDomain_NoCategories_ThrowsNamingTheSupplier()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SupplierMappingExtensions.ToDomainModel(Row(categories: [])));

        Assert.Contains("SUP-1", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DomainValidationException>(exception.InnerException);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(1, 100000)]
    [InlineData(500, 100)]
    public void ToDomain_ZipOutsideRangeOrReversed_ThrowsNamingTheSupplier(int start, int end)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SupplierMappingExtensions.ToDomainModel(Row(areas: [(start, end)])));

        Assert.Contains("SUP-1", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DomainValidationException>(exception.InnerException);
    }

    [Fact]
    public void ToDomain_RatingOutOfRange_ThrowsNamingTheSupplier()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SupplierMappingExtensions.ToDomainModel(Row(rating: 0m)));

        Assert.Contains("SUP-1", exception.Message, StringComparison.Ordinal);
    }
}

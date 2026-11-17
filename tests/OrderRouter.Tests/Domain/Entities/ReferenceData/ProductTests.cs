using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Entities.ReferenceData;

public class ProductTests
{
    [Fact]
    public void Constructor_ValidValues_TrimsCodeAndKeepsCase()
    {
        var product = new Product("  E0601 ", " CPAP Device ", new Category("cpap"));

        Assert.Equal("E0601", product.Code);
        Assert.Equal("CPAP Device", product.Name);
        Assert.Equal(new Category("CPAP"), product.Category);
    }

    [Theory]
    [InlineData(null, "name")]
    [InlineData("", "name")]
    [InlineData("  ", "name")]
    [InlineData("E0601", null)]
    [InlineData("E0601", " ")]
    public void Constructor_EmptyCodeOrName_ThrowsDomainValidationException(string? code, string? name)
    {
        Assert.Throws<DomainValidationException>(() => new Product(code, name, new Category("cpap")));
    }

    [Fact]
    public void Equals_SameCodeDifferentCase_AreNotEqual()
    {
        var upper = new Product("E0601", "A", new Category("x"));
        var same = new Product(" E0601", "B", new Category("y"));
        var lower = new Product("e0601", "A", new Category("x"));

        Assert.Equal(upper, same);
        Assert.Equal(upper.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(upper, lower);
    }
}

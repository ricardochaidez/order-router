using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class CategoryTests
{
    [Fact]
    public void Equals_DifferentCaseAndSurroundingSpaces_AreEqualWithSameHashCode()
    {
        var padded = new Category(" CPAP ");
        var plain = new Category("cpap");

        Assert.Equal(plain, padded);
        Assert.True(padded == plain);
        Assert.Equal(plain.GetHashCode(), padded.GetHashCode());
    }

    [Fact]
    public void HashSet_DifferentSpellingsOfSameCategory_CollapseToOneEntry()
    {
        var set = new HashSet<Category> { new(" CPAP "), new("cpap"), new("Cpap") };

        Assert.Single(set);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Constructor_NullEmptyOrWhitespace_ThrowsDomainValidationException(string? raw)
    {
        var exception = Assert.Throws<DomainValidationException>(() => new Category(raw));

        Assert.Contains("Category", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_MixedCaseWithInnerSpace_KeepsInnerTextAndLowerCases()
    {
        var category = new Category("Hospital Bed");

        Assert.Equal("hospital bed", category.Value);
        Assert.Equal("hospital bed", category.ToString());
    }

    [Fact]
    public void Equals_DifferentCategories_AreNotEqual()
    {
        Assert.NotEqual(new Category("cpap"), new Category("bipap"));
    }

    [Fact]
    public void TryCreate_ValidText_ReturnsTrueWithNormalizedCategory()
    {
        var created = Category.TryCreate(" CPAP ", out var category);

        Assert.True(created);
        Assert.Equal(new Category("cpap"), category);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryCreate_InvalidText_ReturnsFalse(string? raw)
    {
        var created = Category.TryCreate(raw, out var category);

        Assert.False(created);
        Assert.Equal(default, category);
    }

    [Fact]
    public void Value_DefaultInstance_ThrowsInvalidOperationException()
    {
        var category = default(Category);

        Assert.Throws<InvalidOperationException>(() => category.Value);
    }

    [Fact]
    public void ToString_DefaultInstance_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(Category).ToString());
    }
}

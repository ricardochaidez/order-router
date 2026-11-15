using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class SupplierTests
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");

    private static Supplier Build(
        string? id = "S1",
        string? name = "Acme",
        IEnumerable<Category>? categories = null,
        string[]? ranges = null,
        decimal? rating = null,
        bool canMailOrder = false)
        => new(
            id,
            name,
            categories ?? [new Category("cpap")],
            new ServiceArea((ranges ?? ["02114-02212"]).Select(ZipRange.Parse)),
            rating,
            canMailOrder);

    [Theory]
    [InlineData("02114", true)]
    [InlineData("02212", true)]
    [InlineData("02213", false)]
    public void Serves_Zip_IsInclusive(string zip, bool expected)
    {
        Assert.Equal(expected, Build().Serves(ZipCode.Parse(zip)));
    }

    [Fact]
    public void IsMailOnly_MailOrderAndExactlyNationwide_ReturnsTrue()
    {
        Assert.True(Build(ranges: ["00100-99999"], canMailOrder: true).IsMailOnly(nationwide));
    }

    [Fact]
    public void IsMailOnly_NationwideButNoMailOrder_ReturnsFalse()
    {
        Assert.False(Build(ranges: ["00100-99999"], canMailOrder: false).IsMailOnly(nationwide));
    }

    [Fact]
    public void IsMailOnly_NationwidePlusExtraSegment_ReturnsFalse()
    {
        Assert.False(Build(ranges: ["00100-99999", "00050"], canMailOrder: true).IsMailOnly(nationwide));
    }

    [Fact]
    public void IsMailOnly_ConfiguredNationwideRange_UsesTheArgument()
    {
        var configured = ZipRange.Parse("00500-99999");

        Assert.True(Build(ranges: ["00500-99999"], canMailOrder: true).IsMailOnly(configured));
        Assert.False(Build(ranges: ["00100-99999"], canMailOrder: true).IsMailOnly(configured));
    }

    [Fact]
    public void Rating_NoRating_IsNull()
    {
        Assert.Null(Build(rating: null).Rating);
    }

    [Fact]
    public void Rating_ValidRating_IsRounded()
    {
        Assert.Equal(9.26m, Build(rating: 9.255m).Rating!.Value.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Constructor_RatingOutOfRange_ThrowsDomainValidationException(int rating)
    {
        Assert.Throws<DomainValidationException>(() => Build(rating: rating));
    }

    [Fact]
    public void Handles_DifferentCaseAndSpaces_Matches()
    {
        var supplier = Build(categories: [new Category("cpap")]);

        Assert.True(supplier.Handles(new Category(" CPAP ")));
        Assert.False(supplier.Handles(new Category("bipap")));
    }

    [Fact]
    public void Constructor_DuplicateCategoriesAfterNormalization_Collapse()
    {
        Assert.Single(Build(categories: [new Category("CPAP"), new Category(" cpap")]).Categories);
    }

    [Fact]
    public void Constructor_NoCategories_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => Build(categories: []));
        Assert.Throws<DomainValidationException>(() => new Supplier("S1", "Acme", null, new ServiceArea([nationwide]), null, false));
    }

    [Fact]
    public void Constructor_NullServiceArea_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => new Supplier("S1", "Acme", [new Category("a")], null, null, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Constructor_EmptyIdOrName_ThrowsDomainValidationException(string? text)
    {
        Assert.Throws<DomainValidationException>(() => Build(id: text));
        Assert.Throws<DomainValidationException>(() => Build(name: text));
    }

    [Fact]
    public void Constructor_SeveralInvalidFields_MessageListsAll()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new Supplier(" ", "", [], null, 11m, false));

        Assert.Contains("id", exception.Message, StringComparison.Ordinal);
        Assert.Contains("name", exception.Message, StringComparison.Ordinal);
        Assert.Contains("category", exception.Message, StringComparison.Ordinal);
        Assert.Contains("service area", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Rating", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equals_SameNameDifferentIds_AreNotEqual()
    {
        Assert.NotEqual(Build(id: "S1"), Build(id: "S2"));
        Assert.Equal(Build(id: "S1", name: "A"), Build(id: " S1", name: "B"));
        Assert.Equal(Build(id: "S1").GetHashCode(), Build(id: "S1", name: "Z").GetHashCode());
    }
}

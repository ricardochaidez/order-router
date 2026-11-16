using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class ProductCodeLookupTests
{
    private static readonly Product oxygen = new("OX-PORT-024", "Portable oxygen", new Category("oxygen"));

    [Theory]
    [InlineData(" ox-port-024 ")]
    [InlineData("OX-PORT-024")]
    [InlineData("OX- PORT -024")]
    [InlineData("ox-port-024\t")]
    public void Resolve_CaseAndWhitespaceVariants_ResolveToTheProductCategory(string code)
    {
        var lookup = ProductCodeLookup.For([code]);

        var results = lookup.Resolve([oxygen]);

        Assert.True(results[code].TryGetCategory(out var category));
        Assert.Equal(new Category("oxygen"), category);
    }

    [Fact]
    public void For_SpellingsOfOneCode_GiveOneKey()
    {
        var lookup = ProductCodeLookup.For(["OX-PORT-024", " ox-port-024 ", "OX-PORT-024"]);

        var key = Assert.Single(lookup.Keys);
        Assert.Equal("ox-port-024", key.Value);
    }

    [Fact]
    public void For_NullEmptyAndWhitespaceOnlyCodes_GiveNoKeys()
    {
        var lookup = ProductCodeLookup.For([null, "", "   ", "\t"]);

        Assert.Empty(lookup.Keys);
    }

    [Fact]
    public void For_NullCodes_Throws()
        => Assert.Throws<ArgumentNullException>(() => ProductCodeLookup.For(null!));

    [Fact]
    public void Resolve_MixedKnownAndUnknown_GivesOneEntryPerDistinctNonNullInput()
    {
        var lookup = ProductCodeLookup.For(["OX-PORT-024", " ox-port-024 ", "XX-UNKNOWN-999", "", "   ", null, "OX-PORT-024"]);

        var results = lookup.Resolve([oxygen]);

        Assert.Equal(5, results.Count);
        Assert.Equal(new Category("oxygen"), results["OX-PORT-024"].Category);
        Assert.Equal(new Category("oxygen"), results[" ox-port-024 "].Category);
        Assert.Equal(CapabilityResolution.Unknown, results["XX-UNKNOWN-999"]);
        Assert.Equal(CapabilityResolution.Unknown, results[""]);
        Assert.Equal(CapabilityResolution.Unknown, results["   "]);
    }

    [Fact]
    public void Resolve_NoProducts_EveryInputIsUnknown()
    {
        var lookup = ProductCodeLookup.For(["OX-PORT-024", "nope"]);

        var results = lookup.Resolve([]);

        Assert.All(results.Values, value => Assert.False(value.IsKnown));
    }

    [Fact]
    public void Resolve_ProductForAKeyThatWasNotRequested_IsIgnored()
    {
        var lookup = ProductCodeLookup.For(["OX-PORT-02"]);

        var results = lookup.Resolve([oxygen]);

        Assert.False(results["OX-PORT-02"].IsKnown);
    }

    [Fact]
    public void Resolve_TwoProductsForOneKey_ThrowsNamingBothCodes()
    {
        var lookup = ProductCodeLookup.For(["abc-1"]);
        Product[] ambiguous = [new("ABC-1", "One", new Category("cpap")), new("abc-1", "Two", new Category("cpap"))];

        var exception = Assert.Throws<InvalidOperationException>(() => lookup.Resolve(ambiguous));

        Assert.Contains("\"ABC-1\"", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"abc-1\"", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_TwoProductsForAKeyThatWasNotRequested_DoesNotThrow()
    {
        var lookup = ProductCodeLookup.For(["OX-PORT-024"]);
        Product[] products = [oxygen, new("ABC-1", "One", new Category("cpap")), new("abc-1", "Two", new Category("cpap"))];

        var results = lookup.Resolve(products);

        Assert.True(results["OX-PORT-024"].IsKnown);
    }

    [Fact]
    public void Resolve_NullProducts_Throws()
        => Assert.Throws<ArgumentNullException>(() => ProductCodeLookup.For(["A"]).Resolve(null!));
}

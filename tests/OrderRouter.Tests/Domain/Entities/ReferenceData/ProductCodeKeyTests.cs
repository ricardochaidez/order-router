using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Tests.Domain.Entities.ReferenceData;

public class ProductCodeKeyTests
{
    [Theory]
    [InlineData(" ox-port-024 ")]
    [InlineData("OX- PORT-024")]
    [InlineData("OX-PORT-024")]
    [InlineData("ox-port-024\t")]
    [InlineData("\r\nOX-PORT-024\n")]
    [InlineData("O X - P O R T - 0 2 4")]
    public void TryCreate_CaseAndWhitespaceVariants_AllGiveTheSameKey(string raw)
    {
        var created = ProductCodeKey.TryCreate(raw, out var key);

        Assert.True(created);
        Assert.Equal("ox-port-024", key.Value);
        Assert.Equal("ox-port-024", key.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void TryCreate_NullEmptyOrWhitespaceOnly_ReturnsFalse(string? raw)
    {
        var created = ProductCodeKey.TryCreate(raw, out var key);

        Assert.False(created);
        Assert.Equal(default, key);
    }

    [Fact]
    public void Equals_SameKeyFromDifferentSpellings_AreEqualWithSameHashCode()
    {
        ProductCodeKey.TryCreate("OX-PORT-024", out var first);
        ProductCodeKey.TryCreate(" ox- port-024 ", out var second);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_PrefixOfAnotherCode_AreNotEqual()
    {
        ProductCodeKey.TryCreate("OX-PORT-024", out var shorter);
        ProductCodeKey.TryCreate("OX-PORT-024-PRO", out var longer);

        Assert.NotEqual(shorter, longer);
    }

    [Fact]
    public void TryCreate_NonAsciiLetters_AreLowerCasedWithInvariantCulture()
    {
        ProductCodeKey.TryCreate("ÄÖÜ-1", out var key);

        Assert.Equal("äöü-1", key.Value);
    }

    [Fact]
    public void TryCreate_TurkishDottedCapitalI_IsNotCultureDependent()
    {
        ProductCodeKey.TryCreate("I-1", out var key);

        Assert.Equal("i-1", key.Value);
    }

    [Fact]
    public void Value_DefaultInstance_ThrowsInvalidOperationException()
    {
        var key = default(ProductCodeKey);

        Assert.Throws<InvalidOperationException>(() => key.Value);
    }

    [Fact]
    public void ToString_DefaultInstance_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(ProductCodeKey).ToString());
    }
}

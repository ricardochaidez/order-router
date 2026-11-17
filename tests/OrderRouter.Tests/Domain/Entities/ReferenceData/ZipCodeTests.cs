using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Entities.ReferenceData;

public class ZipCodeTests
{
    [Theory]
    [InlineData("02130", 2130)]
    [InlineData("00000", 0)]
    [InlineData("99999", 99999)]
    public void Parse_FiveDigits_ReturnsZip(string text, int number)
    {
        var zip = ZipCode.Parse(text);

        Assert.Equal(number, zip.Number);
        Assert.Equal(text, zip.Value);
    }

    [Theory]
    [InlineData("2130")]
    [InlineData("0213A")]
    [InlineData(" 02130")]
    [InlineData("02130 ")]
    [InlineData("021300")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("٠٢١٣٠")]
    public void Parse_NotExactlyFiveAsciiDigits_ThrowsDomainValidationException(string? text)
    {
        Assert.Throws<DomainValidationException>(() => ZipCode.Parse(text));
        Assert.False(ZipCode.TryParse(text, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100000)]
    public void FromNumber_OutOfBounds_ThrowsDomainValidationException(int number)
    {
        Assert.Throws<DomainValidationException>(() => ZipCode.FromNumber(number));
    }

    [Fact]
    public void FromNumber_FourDigitNumber_PadsToFiveDigits()
    {
        Assert.Equal("02130", ZipCode.FromNumber(2130).Value);
    }

    [Fact]
    public void Equals_SameZipFromDifferentSources_AreEqual()
    {
        Assert.Equal(ZipCode.Parse("02130"), ZipCode.FromNumber(2130));
        Assert.Equal(ZipCode.Parse("02130").GetHashCode(), ZipCode.FromNumber(2130).GetHashCode());
    }

    [Fact]
    public void CompareTo_LowerZip_ReturnsNegative()
    {
        Assert.True(ZipCode.Parse("00100").CompareTo(ZipCode.Parse("99999")) < 0);
        Assert.Equal("02130", ZipCode.Parse("02130").ToString());
    }
}

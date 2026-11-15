using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class ZipRangeTests
{
    [Theory]
    [InlineData("02114", true)]
    [InlineData("02212", true)]
    [InlineData("02150", true)]
    [InlineData("02213", false)]
    [InlineData("02113", false)]
    public void Contains_Zip_IsInclusiveOnBothEnds(string zip, bool expected)
    {
        var range = ZipRange.Parse("02114-02212");

        Assert.Equal(expected, range.Contains(ZipCode.Parse(zip)));
    }

    [Fact]
    public void Constructor_StartGreaterThanEnd_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => new ZipRange(ZipCode.Parse("02213"), ZipCode.Parse("02114")));
    }

    [Fact]
    public void Single_Zip_ContainsOnlyThatZip()
    {
        var range = ZipRange.Single(ZipCode.Parse("02130"));

        Assert.True(range.Contains(ZipCode.Parse("02130")));
        Assert.False(range.Contains(ZipCode.Parse("02131")));
    }

    [Theory]
    [InlineData("02114-02212")]
    [InlineData("02130")]
    public void Parse_ValidText_Succeeds(string text)
    {
        Assert.True(ZipRange.TryParse(text, out var range));
        Assert.Equal(ZipRange.Parse(text), range);
    }

    [Theory]
    [InlineData("2114-2212")]
    [InlineData("02114 - 02212")]
    [InlineData("02114-")]
    [InlineData("02212-02114")]
    [InlineData("02114_02212")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_InvalidText_ThrowsDomainValidationException(string? text)
    {
        Assert.Throws<DomainValidationException>(() => ZipRange.Parse(text));
        Assert.False(ZipRange.TryParse(text, out _));
    }

    [Fact]
    public void ToString_Range_RoundTripsThroughParse()
    {
        var range = ZipRange.Parse("00100-99999");

        Assert.Equal("00100-99999", range.ToString());
        Assert.Equal(range, ZipRange.Parse(range.ToString()));
    }
}

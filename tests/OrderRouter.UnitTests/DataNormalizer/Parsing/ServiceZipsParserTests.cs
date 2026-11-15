using OrderRouter.DataNormalizer.Parsing;

namespace OrderRouter.UnitTests.DataNormalizer.Parsing;

public class ServiceZipsParserTests
{
    [Fact]
    public void TryParse_FourDigitZipAndRange_PadsLeadingZeros()
    {
        var success = ServiceZipsParser.TryParse("2130, 2114-2212", out var ranges, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(["02130-02130", "02114-02212"], ranges.Select(range => range.ToString()));
    }

    [Fact]
    public void TryParse_MixedListAndRanges_KeepsSourceOrder()
    {
        var success = ServiceZipsParser.TryParse("10059-10103, 90117, 60699", out var ranges, out _);

        Assert.True(success);
        Assert.Equal(["10059-10103", "90117-90117", "60699-60699"], ranges.Select(range => range.ToString()));
    }

    [Fact]
    public void TryParse_ExtraWhitespace_IsTrimmed()
    {
        var success = ServiceZipsParser.TryParse("  02130 ,\t 02114 - 02212  ", out var ranges, out _);

        Assert.True(success);
        Assert.Equal(2, ranges.Count);
    }

    [Theory]
    [InlineData("00001", "00001-00001")]
    [InlineData("1", "00001-00001")]
    [InlineData("00100-99999", "00100-99999")]
    public void TryParse_SentinelAndNationwide_AreValid(string cell, string expected)
    {
        var success = ServiceZipsParser.TryParse(cell, out var ranges, out _);

        Assert.True(success);
        Assert.Equal(expected, Assert.Single(ranges).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("02130,")]
    [InlineData("02130,,02131")]
    [InlineData("ABCDE")]
    [InlineData("123456")]
    [InlineData("5-3")]
    [InlineData("02114-")]
    [InlineData("-02114")]
    [InlineData("1-2-3")]
    [InlineData("0213a")]
    [InlineData("02114-123456")]
    public void TryParse_InvalidCell_ReturnsErrorAndNoRanges(string? cell)
    {
        var success = ServiceZipsParser.TryParse(cell, out var ranges, out var error);

        Assert.False(success);
        Assert.NotEmpty(error!);
        Assert.Empty(ranges);
    }

    [Fact]
    public void TryParse_OneBadTokenAmongGoodOnes_RejectsWholeCell()
    {
        var success = ServiceZipsParser.TryParse("02130, XYZ, 02131", out var ranges, out var error);

        Assert.False(success);
        Assert.Contains("XYZ", error);
        Assert.Empty(ranges);
    }
}

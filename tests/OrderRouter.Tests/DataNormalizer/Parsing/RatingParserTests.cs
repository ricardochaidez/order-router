using OrderRouter.DataNormalizer.Parsing;

namespace OrderRouter.Tests.DataNormalizer.Parsing;

public class RatingParserTests
{
    [Theory]
    [InlineData("10", 10)]
    [InlineData("10.0", 10)]
    [InlineData("6", 6)]
    [InlineData(" 6.7 ", 6.7)]
    public void TryParse_Number_ReturnsDecimal(string cell, double expected)
    {
        var success = RatingParser.TryParse(cell, out var rating, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal((decimal)expected, rating);
    }

    [Theory]
    [InlineData("no ratings yet")]
    [InlineData("NO RATINGS YET")]
    [InlineData("  No Ratings Yet ")]
    public void TryParse_UnratedMarker_ReturnsNull(string cell)
    {
        var success = RatingParser.TryParse(cell, out var rating, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Null(rating);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("7,5")]
    public void TryParse_Unparseable_ReturnsError(string? cell)
    {
        var success = RatingParser.TryParse(cell, out var rating, out var error);

        Assert.False(success);
        Assert.NotEmpty(error!);
        Assert.Null(rating);
    }

    [Fact]
    public void TryParse_OutOfRangeNumber_IsLeftToTheDomain()
    {
        var success = RatingParser.TryParse("11", out var rating, out _);

        Assert.True(success);
        Assert.Equal(11m, rating);
    }
}

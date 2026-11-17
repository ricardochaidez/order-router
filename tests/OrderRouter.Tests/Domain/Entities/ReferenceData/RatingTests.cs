using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Entities.ReferenceData;

public class RatingTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("1.00")]
    [InlineData("9.25")]
    public void Constructor_InRange_Succeeds(string text)
    {
        var rating = new Rating(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));

        Assert.InRange(rating.Value, 1m, 10m);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("0.99")]
    [InlineData("10.01")]
    [InlineData("0.996")]
    [InlineData("10.004")]
    public void Constructor_OutOfRange_ThrowsDomainValidationException(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<DomainValidationException>(() => new Rating(value));
    }

    [Theory]
    [InlineData("9.255", "9.26")]
    [InlineData("9.254", "9.25")]
    [InlineData("9.245", "9.25")]
    [InlineData("9.2", "9.2")]
    public void Constructor_MoreThanTwoDecimals_RoundsHalvesUp(string raw, string expected)
    {
        var rating = new Rating(decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rating.Value);
    }

    [Fact]
    public void Equals_NumericallyEqualValues_AreEqual()
    {
        Assert.Equal(new Rating(10m), new Rating(10.0m));
        Assert.Equal(new Rating(9.255m), new Rating(9.26m));
        Assert.Equal(new Rating(10m).GetHashCode(), new Rating(10.00m).GetHashCode());
        Assert.NotEqual(new Rating(9m), new Rating(10m));
    }

    [Fact]
    public void ToString_Rating_ReturnsRoundedValue()
    {
        Assert.Equal("9.26", new Rating(9.255m).ToString());
    }
}

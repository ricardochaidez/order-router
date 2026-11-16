using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.UnitTests.Domain.Routing.Experiments;

public class ExperimentTargetingTests
{
    private static ExperimentTargeting Targeting(string[]? callers = null, string[]? prefixes = null, string[]? ranges = null)
        => new(callers ?? [], prefixes ?? [], [.. (ranges ?? []).Select(ZipRange.Parse)]);

    [Fact]
    public void Everyone_MatchesAnyCallerAndZip()
    {
        Assert.True(ExperimentTargeting.Everyone.Matches(null, ZipCode.Parse("10015")));
        Assert.True(ExperimentTargeting.Everyone.Matches("anyone", ZipCode.Parse("00000")));
    }

    [Theory]
    [InlineData("021", "02114", true)]
    [InlineData("021", "10015", false)]
    [InlineData("02114", "02114", true)]
    [InlineData("0", "00100", true)]
    [InlineData("1", "02114", false)]
    public void MatchesZip_Prefix_ComparesAgainstTheFiveDigitText(string prefix, string zip, bool expected)
        => Assert.Equal(expected, Targeting(prefixes: [prefix]).MatchesZip(ZipCode.Parse(zip)));

    [Theory]
    [InlineData("30000", true)]
    [InlineData("30500", true)]
    [InlineData("30999", true)]
    [InlineData("29999", false)]
    [InlineData("31000", false)]
    public void MatchesZip_Range_IsInclusiveOnBothEnds(string zip, bool expected)
        => Assert.Equal(expected, Targeting(ranges: ["30000-30999"]).MatchesZip(ZipCode.Parse(zip)));

    [Fact]
    public void MatchesZip_PrefixOrRange_EitherIsEnough()
    {
        var targeting = Targeting(prefixes: ["021"], ranges: ["30000-30999"]);

        Assert.True(targeting.MatchesZip(ZipCode.Parse("02114")));
        Assert.True(targeting.MatchesZip(ZipCode.Parse("30500")));
        Assert.False(targeting.MatchesZip(ZipCode.Parse("10015")));
    }

    [Fact]
    public void MatchesCaller_EmptyList_MatchesEveryoneIncludingNoCaller()
    {
        Assert.True(Targeting().MatchesCaller(null));
        Assert.True(Targeting().MatchesCaller("x"));
    }

    [Fact]
    public void MatchesCaller_NonEmptyList_NoCallerDoesNotMatch()
        => Assert.False(Targeting(callers: ["portal-client"]).MatchesCaller(null));

    [Fact]
    public void MatchesCaller_IsOrdinalAndCaseSensitive()
    {
        var targeting = Targeting(callers: ["portal-client"]);

        Assert.True(targeting.MatchesCaller("portal-client"));
        Assert.False(targeting.MatchesCaller("Portal-Client"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("021140")]
    [InlineData("02a")]
    [InlineData("０２１")]
    public void Constructor_BadPrefix_Throws(string prefix)
        => Assert.Throws<DomainValidationException>(() => Targeting(prefixes: [prefix]));

    [Fact]
    public void Constructor_FourDigitPrefix_IsValid()
        => Assert.Equal(["0210"], Targeting(prefixes: ["0210"]).ZipPrefixes);

    [Fact]
    public void Constructor_BlankCaller_ThrowsWithoutEchoingIt()
    {
        var exception = Assert.Throws<DomainValidationException>(() => Targeting(callers: ["ok-caller", " "]));

        Assert.DoesNotContain("ok-caller", exception.Message);
    }

    [Fact]
    public void Constructor_CopiesTheLists()
    {
        var callers = new List<string> { "a" };
        var targeting = new ExperimentTargeting(callers, [], []);

        callers.Add("b");

        Assert.Equal(["a"], targeting.Callers);
    }
}

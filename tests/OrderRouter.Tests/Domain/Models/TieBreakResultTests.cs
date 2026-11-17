using OrderRouter.Domain.Models;
using static OrderRouter.Tests.TestData.TieBreakTestData;

namespace OrderRouter.Tests.Domain.Models;

public class TieBreakResultTests
{
    [Fact]
    public void Constructor_ChosenNotInCandidates_Throws()
    {
        Assert.Throws<DomainValidationException>(() => new TieBreakResult(Plan("SUP-1"), [Plan("SUP-2"), Plan("SUP-3")]));
    }

    [Fact]
    public void Constructor_EmptyCandidates_Throws()
    {
        Assert.Throws<DomainValidationException>(() => new TieBreakResult(Plan("SUP-1"), []));
    }

    [Fact]
    public void UsedRandom_MatchesCandidateCount()
    {
        var one = Plan("SUP-1");
        var two = Plan("SUP-2");

        Assert.False(new TieBreakResult(one, [one]).UsedRandom);
        Assert.True(new TieBreakResult(one, [one, two]).UsedRandom);
    }
}

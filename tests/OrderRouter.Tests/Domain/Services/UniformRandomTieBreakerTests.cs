using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;
using static OrderRouter.Tests.TestData.TieBreakTestData;

namespace OrderRouter.Tests.Domain.Services;

public class UniformRandomTieBreakerTests
{
    [Fact]
    public void Pick_ThreeTiedAndSourceReturnsTwo_ChoosesThirdPlan()
    {
        var plans = Plans(3);
        var source = new FixedRandomSource(2);

        var result = new UniformRandomTieBreaker(source).Pick(plans);

        Assert.Same(plans[2], result.Chosen);
        Assert.Equal(plans, result.TiedCandidates);
        Assert.True(result.HasTie);
        Assert.Equal(1, source.CallCount);
        Assert.Equal([3], source.RequestedBounds);
    }

    [Fact]
    public void Pick_SinglePlan_ReturnsItWithoutUsingRandomness()
    {
        var plans = Plans(1);
        var source = new FixedRandomSource(0);

        var result = new UniformRandomTieBreaker(source).Pick(plans);

        Assert.Same(plans[0], result.Chosen);
        Assert.False(result.HasTie);
        Assert.Equal(0, source.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Pick_BoundaryIndexes_ChooseFirstAndLast(int index)
    {
        var plans = Plans(4);

        var result = new UniformRandomTieBreaker(new FixedRandomSource(index)).Pick(plans);

        Assert.Same(plans[index], result.Chosen);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Pick_SourceReturnsOutOfRange_Throws(int index)
    {
        var tieBreaker = new UniformRandomTieBreaker(new FixedRandomSource(index));

        Assert.Throws<InvalidOperationException>(() => tieBreaker.Pick(Plans(3)));
    }

    [Fact]
    public void Pick_EmptyList_Throws()
    {
        var tieBreaker = new UniformRandomTieBreaker(new FixedRandomSource(0));

        Assert.Throws<ArgumentException>(() => tieBreaker.Pick([]));
    }

    [Fact]
    public void Pick_NullList_Throws()
    {
        var tieBreaker = new UniformRandomTieBreaker(new FixedRandomSource(0));

        Assert.Throws<ArgumentNullException>(() => tieBreaker.Pick(null!));
    }

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UniformRandomTieBreaker(null!));
    }

    [Fact]
    public void Pick_CallerMutatesListAfterwards_ResultIsUnchanged()
    {
        var plans = Plans(3).ToList();

        var result = new UniformRandomTieBreaker(new FixedRandomSource(1)).Pick(plans);
        plans.Clear();

        Assert.Equal(3, result.TiedCandidates.Count);
    }
}

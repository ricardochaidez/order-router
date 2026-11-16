using OrderRouter.Infrastructure.Randomness;

namespace OrderRouter.UnitTests.Infrastructure.Randomness;

public class SystemRandomSourceTests
{
    private readonly SystemRandomSource source = new();

    [Fact]
    public void NextInt_BoundOfOne_AlwaysReturnsZero()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(0, source.NextInt(1));
        }
    }

    [Fact]
    public void NextInt_BoundOfThree_StaysInRangeAndHitsEveryValue()
    {
        var seen = new HashSet<int>();

        for (var i = 0; i < 500; i++)
        {
            var value = source.NextInt(3);
            Assert.InRange(value, 0, 2);
            seen.Add(value);
        }

        Assert.Equal([0, 1, 2], seen.Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_BoundBelowOne_Throws(int bound)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => source.NextInt(bound));
    }
}

using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Routing.TieBreaking;

namespace OrderRouter.UnitTests.Domain.Routing.TieBreaking;

/// <summary>Test double: always returns one value, counts calls and remembers the bounds it was asked for (NFR-08a).</summary>
internal sealed class FixedRandomSource(int value) : IRandomSource
{
    public int CallCount { get; private set; }

    public List<int> RequestedBounds { get; } = [];

    public int NextInt(int maxExclusive)
    {
        CallCount++;
        RequestedBounds.Add(maxExclusive);

        return value;
    }
}

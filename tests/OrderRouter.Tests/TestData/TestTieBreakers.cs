using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Services;

namespace OrderRouter.Tests.TestData;

/// <summary>The EXP-4 treatment tie-breaker for building a <see cref="OrderRouter.Domain.Services.Policies.RoutingPolicyFactory"/> in tests.</summary>
internal static class TestTieBreakers
{
    /// <summary>The treatment (random) tie-breaker with a source that always picks the first tied plan. Core routing never uses it.</summary>
    public static ITieBreaker Treatment() => new UniformRandomTieBreaker(new FixedRandomSource(0));
}

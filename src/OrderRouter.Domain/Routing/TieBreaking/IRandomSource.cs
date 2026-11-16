namespace OrderRouter.Domain.Routing.TieBreaking;

/// <summary>
/// The only source of randomness in the domain (NFR-08a, ADR-0001). Tests inject a fixed source for exact
/// assertions; production uses a truly random one.
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns a random integer n with 0 &lt;= n &lt; <paramref name="maxExclusive"/>.</summary>
    /// <param name="maxExclusive">Exclusive upper bound; 1 or more.</param>
    int NextInt(int maxExclusive);
}

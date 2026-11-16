namespace OrderRouter.Application.Routing;

/// <summary>
/// Turns a caller ID into a pseudonym that can be stored and logged. The raw caller ID is never stored or logged
/// (NFR-11, ADR-0005, ADR-0007). The same caller always gets the same hash, so the hash can be used to check
/// ownership of a stored order (SC-31) and as <c>caller_hash</c> in the decision log (AB-04).
/// </summary>
public interface ICallerHasher
{
    /// <summary>Hashes a caller ID.</summary>
    /// <param name="callerId">The authenticated caller ID (JWT <c>sub</c>, client ID or the dev key); not blank.</param>
    /// <returns>The hash as lowercase hexadecimal text.</returns>
    /// <exception cref="ArgumentException"><paramref name="callerId"/> is null or blank.</exception>
    string Hash(string callerId);
}

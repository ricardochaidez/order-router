using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OrderRouter.Domain.Routing.Experiments;

/// <summary>
/// Maps a randomization unit to a stable bucket from 0 to 99 (AB-01, ADR-0005).
/// </summary>
/// <remarks>
/// It uses SHA-256 from the BCL (<c>System.Security.Cryptography</c>) because the result must be identical in every
/// process, instance and restart. <see cref="string.GetHashCode()"/> is randomized per process, so it is never used.
/// The first 8 bytes are read big-endian explicitly so the result does not depend on the CPU's byte order.
/// </remarks>
public static class ExperimentBucketer
{
    /// <summary>Number of buckets: one per percentage point.</summary>
    public const int BucketCount = 100;

    /// <summary>Computes <c>SHA256("{experimentId}:{salt}:{unit}")</c> as UInt64 (first 8 bytes, big-endian) modulo 100.</summary>
    /// <param name="experimentId">The experiment key.</param>
    /// <param name="salt">The experiment's salt; changing it reshuffles every unit.</param>
    /// <param name="unit">The unit of randomization: the caller ID, or the order ID when there is no caller.</param>
    /// <returns>A bucket from 0 to 99.</returns>
    public static int Bucket(string experimentId, string salt, string unit)
    {
        ArgumentNullException.ThrowIfNull(experimentId);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(unit);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experimentId}:{salt}:{unit}"));

        return (int)(BinaryPrimitives.ReadUInt64BigEndian(hash) % BucketCount);
    }
}

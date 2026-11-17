using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Sections;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// HMAC-SHA256 of the caller ID with a server secret (ADR-0005, ADR-0007). A keyed hash is used instead of a plain
/// hash so that, without the secret, nobody can confirm a guessed caller ID by hashing it. It uses
/// <see cref="HMACSHA256"/> from the .NET base library; the secret is read once at construction.
/// </summary>
public sealed class HmacCallerHasher : ICallerHasher
{
    private readonly byte[] key;

    /// <summary>Creates the hasher.</summary>
    /// <param name="options">Holds the secret; startup validation guarantees it is present.</param>
    /// <exception cref="InvalidOperationException">The secret is missing (only when startup validation was bypassed).</exception>
    public HmacCallerHasher(IOptions<CallerHashingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var secret = options.Value.Secret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"{CallerHashingOptions.SecretKey} is not configured.");
        }

        key = Encoding.UTF8.GetBytes(secret);
    }

    /// <inheritdoc />
    public string Hash(string callerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerId);

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(callerId)));
    }
}

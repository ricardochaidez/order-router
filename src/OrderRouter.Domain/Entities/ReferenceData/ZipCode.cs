using System.Diagnostics.CodeAnalysis;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// A US ZIP code: exactly 5 ASCII digits. Strict on purpose: no trimming and no padding, so a
/// 4-digit ZIP such as <c>"2130"</c> is rejected (AC3). Padding is the normalizer's job (OR-003);
/// the seeder (OR-005) must fail on bad rows. <c>00000</c>–<c>00099</c> are valid (DQ-14).
/// </summary>
/// <remarks>
/// Stored as a number so ranges compare cheaply and OR-006 can map <c>int</c> columns.
/// <c>default(ZipCode)</c> is <c>00000</c>, which is itself a valid ZIP.
/// </remarks>
/// <example>
/// <code>
/// ZipCode.Parse("02130").Number; // 2130
/// </code>
/// </example>
public readonly record struct ZipCode : IComparable<ZipCode>
{
    private const int maxNumber = 99999;

    private readonly int number;

    private ZipCode(int number) => this.number = number;

    /// <summary>The 5-digit, zero-padded text form.</summary>
    public string Value => number.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The numeric value (0–99999), used for range comparison.</summary>
    public int Number => number;

    /// <summary>Parses exactly 5 ASCII digits.</summary>
    /// <param name="value">Candidate text.</param>
    /// <exception cref="DomainValidationException">The text is not exactly 5 ASCII digits.</exception>
    public static ZipCode Parse(string? value)
        => TryParse(value, out var zip)
            ? zip
            : throw new DomainValidationException(
                $"ZIP code must be exactly 5 digits (received {(value is null ? "null" : $"\"{value}\"")}).");

    /// <summary>Tries to parse exactly 5 ASCII digits without throwing.</summary>
    /// <param name="value">Candidate text.</param>
    /// <param name="zip">The parsed ZIP, or <c>default</c> on failure.</param>
    /// <returns><c>true</c> when valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out ZipCode zip)
    {
        zip = default;

        if (value is not { Length: 5 })
        {
            return false;
        }

        var parsed = 0;

        foreach (var character in value)
        {
            // Ascii only: char.IsDigit would accept Unicode digits such as Arabic-Indic zero.
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }

            parsed = (parsed * 10) + (character - '0');
        }

        zip = new ZipCode(parsed);

        return true;
    }

    /// <summary>Creates a ZIP from its numeric form (ADR-0002 stores ZIPs as <c>int</c>).</summary>
    /// <param name="number">A value from 0 to 99999; <c>2130</c> becomes <c>"02130"</c>.</param>
    /// <exception cref="DomainValidationException">The number is outside 0–99999.</exception>
    public static ZipCode FromNumber(int number)
        => number is >= 0 and <= maxNumber
            ? new ZipCode(number)
            : throw new DomainValidationException($"ZIP number must be between 0 and {maxNumber} (received {number}).");

    /// <inheritdoc />
    public int CompareTo(ZipCode other) => number.CompareTo(other.number);

    /// <summary>Returns the 5-digit text.</summary>
    public override string ToString() => Value;
}

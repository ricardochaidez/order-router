using System.Diagnostics.CodeAnalysis;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// A product or supplier category such as <c>cpap</c>. The value is trimmed and lower-cased
/// (invariant culture) on creation, so <c>" CPAP "</c> and <c>"cpap"</c> are the same category.
/// </summary>
/// <remarks>
/// Equality and hash code are ordinal on the normalized value, so <see cref="HashSet{T}"/>
/// needs no custom comparer (Technical Requirements §5.1).
/// <para>
/// A struct cannot forbid <c>default(Category)</c>. A default instance has no value, and reading
/// <see cref="Value"/> on it throws <see cref="InvalidOperationException"/> instead of returning <c>null</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new Category(" CPAP ") == new Category("cpap"); // true
/// </code>
/// </example>
public readonly record struct Category
{
    private readonly string? value;

    /// <summary>Creates a category from raw text.</summary>
    /// <param name="value">Raw category text; trimmed and lower-cased.</param>
    /// <exception cref="DomainValidationException"><paramref name="value"/> is null, empty or whitespace.</exception>
    public Category(string? value)
    {
        this.value = Normalize(value)
            ?? throw new DomainValidationException(
                $"Category must not be null, empty or whitespace (received {Describe(value)}).");
    }

    /// <summary>The normalized (trimmed, lower-case) category key.</summary>
    /// <exception cref="InvalidOperationException">This is <c>default(Category)</c>.</exception>
    public string Value => value
        ?? throw new InvalidOperationException("Category was not initialized; use the constructor or TryCreate.");

    /// <summary>Tries to create a category without throwing, for callers that only need a yes/no.</summary>
    /// <param name="value">Raw category text.</param>
    /// <param name="category">The created category, or <c>default</c> when this returns <c>false</c>.</param>
    /// <returns><c>true</c> when <paramref name="value"/> is a valid category.</returns>
    public static bool TryCreate(string? value, out Category category)
    {
        if (Normalize(value) is { } normalized)
        {
            category = new Category(normalized);
            return true;
        }

        category = default;
        return false;
    }

    /// <summary>Returns the normalized value.</summary>
    public override string ToString() => value ?? string.Empty;

    private static string? Normalize(string? raw)
    {
        var trimmed = raw?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    private static string Describe(string? raw) => raw is null ? "null" : $"\"{raw}\"";
}

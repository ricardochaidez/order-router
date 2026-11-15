using OrderRouter.Domain.Common;

namespace OrderRouter.Domain.ReferenceData;

/// <summary>
/// A catalog product the router can look up by code. The code is an opaque trimmed string whose
/// case is kept and never re-formatted (DQ-09); case-insensitive lookup is OR-006's job (FR-05).
/// </summary>
public sealed class Product : IEquatable<Product>
{
    /// <summary>Creates a product.</summary>
    /// <param name="code">Product code; trimmed, must not be empty.</param>
    /// <param name="name">Display name; trimmed, must not be empty.</param>
    /// <param name="category">The product's category.</param>
    /// <exception cref="DomainValidationException">The code or name is empty.</exception>
    public Product(string? code, string? name, Category category)
    {
        var errors = new List<string>();
        var trimmedCode = code?.Trim();
        var trimmedName = name?.Trim();

        if (string.IsNullOrEmpty(trimmedCode))
        {
            errors.Add("product code must not be empty");
        }

        if (string.IsNullOrEmpty(trimmedName))
        {
            errors.Add("product name must not be empty");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid product: {string.Join("; ", errors)}.");
        }

        Code = trimmedCode!;
        Name = trimmedName!;
        Category = category;
    }

    /// <summary>The product code, case preserved.</summary>
    public string Code { get; }

    /// <summary>The display name.</summary>
    public string Name { get; }

    /// <summary>The product's category.</summary>
    public Category Category { get; }

    /// <inheritdoc />
    public bool Equals(Product? other) => other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Product);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
}

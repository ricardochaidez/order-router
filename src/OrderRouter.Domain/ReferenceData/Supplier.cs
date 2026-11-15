using OrderRouter.Domain.Common;

namespace OrderRouter.Domain.ReferenceData;

/// <summary>
/// A DME supplier: root of the read-only reference-data aggregate. Identity is the opaque
/// <see cref="Id"/> only, so two suppliers with the same name are distinct (DQ-10).
/// </summary>
public sealed class Supplier : IEquatable<Supplier>
{
    private readonly HashSet<Category> categories;

    /// <summary>Creates a supplier, reporting every invalid field in one exception.</summary>
    /// <param name="id">Opaque identifier; trimmed, never re-formatted (DQ-09).</param>
    /// <param name="name">Display name.</param>
    /// <param name="categories">At least one category; duplicates collapse.</param>
    /// <param name="serviceArea">The served ZIP ranges.</param>
    /// <param name="rating">Raw rating (1–10) or <c>null</c> when unrated; never <c>0</c> (AC7).</param>
    /// <param name="canMailOrder">Whether the supplier can ship by mail (SC-07).</param>
    /// <exception cref="DomainValidationException">One or more fields are invalid (AC8 included).</exception>
    public Supplier(
        string? id,
        string? name,
        IEnumerable<Category>? categories,
        ServiceArea? serviceArea,
        decimal? rating,
        bool canMailOrder)
    {
        var errors = new List<string>();
        var trimmedId = id?.Trim();
        var trimmedName = name?.Trim();
        var categorySet = categories is null ? [] : categories.ToHashSet();
        Rating? parsedRating = null;

        if (string.IsNullOrEmpty(trimmedId))
        {
            errors.Add("supplier id must not be empty");
        }

        if (string.IsNullOrEmpty(trimmedName))
        {
            errors.Add("supplier name must not be empty");
        }

        if (categorySet.Count == 0)
        {
            errors.Add("supplier must have at least one category");
        }

        if (serviceArea is null)
        {
            errors.Add("supplier must have a service area");
        }

        if (rating is { } raw)
        {
            try
            {
                parsedRating = new Rating(raw);
            }
            catch (DomainValidationException exception)
            {
                errors.Add(exception.Message.TrimEnd('.'));
            }
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid supplier: {string.Join("; ", errors)}.");
        }

        Id = trimmedId!;
        Name = trimmedName!;
        this.categories = categorySet;
        ServiceArea = serviceArea!;
        Rating = parsedRating;
        CanMailOrder = canMailOrder;
    }

    /// <summary>Opaque supplier identifier.</summary>
    public string Id { get; }

    /// <summary>Display name (not unique, DQ-10).</summary>
    public string Name { get; }

    /// <summary>The categories this supplier handles.</summary>
    public IReadOnlySet<Category> Categories => categories;

    /// <summary>The ZIP ranges this supplier serves.</summary>
    public ServiceArea ServiceArea { get; }

    /// <summary>The rating, or <c>null</c> when unrated.</summary>
    public Rating? Rating { get; }

    /// <summary>Whether the supplier can ship by mail.</summary>
    public bool CanMailOrder { get; }

    /// <summary>Do you handle this category?</summary>
    /// <param name="category">The category to test.</param>
    public bool Handles(Category category) => categories.Contains(category);

    /// <summary>Do you serve this ZIP?</summary>
    /// <param name="zip">The ZIP to test.</param>
    public bool Serves(ZipCode zip) => ServiceArea.Contains(zip);

    /// <summary>
    /// Are you mail-only? True when the supplier can mail order and its service area is exactly the
    /// nationwide range (SC-09). A supplier with <c>can_mail_order = n</c> is never mail-only (SC-07).
    /// </summary>
    /// <param name="nationwideRange">
    /// The configured nationwide range (FR-09). Passed in because the domain never reads configuration
    /// and the value is never stored (ADR-0002).
    /// </param>
    public bool IsMailOnly(ZipRange nationwideRange) => CanMailOrder && ServiceArea.IsExactly(nationwideRange);

    /// <inheritdoc />
    public bool Equals(Supplier? other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Supplier);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}

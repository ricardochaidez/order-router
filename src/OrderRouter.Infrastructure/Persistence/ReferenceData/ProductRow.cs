namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>Persistence shape of the <c>products</c> table. Not a domain object; OR-006 projects it into <c>Product</c>.</summary>
public sealed class ProductRow
{
    /// <summary>Product code (primary key, case-sensitive; case-insensitive lookup is OR-006's job, FR-05).</summary>
    public required string ProductCode { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Category value. No foreign key: it joins <c>supplier_categories.category</c> by value (ADR-0002).</summary>
    public required string Category { get; set; }
}

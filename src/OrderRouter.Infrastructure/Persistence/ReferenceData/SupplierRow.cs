namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>
/// Persistence shape of the <c>suppliers</c> table (ADR-0002). This is <b>not</b> a domain object:
/// OR-006 projects rows into <c>Supplier</c>, whose constructor enforces the invariants.
/// </summary>
/// <remarks>
/// There is deliberately no mail-only column. It is derived at read time from the configurable
/// nationwide ZIP range (SC-09, FR-09), so it can never go stale.
/// </remarks>
public sealed class SupplierRow
{
    /// <summary>Supplier identifier from the seed data (primary key).</summary>
    public required string SupplierId { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Rating from 1.00 to 10.00, or <c>null</c> when the supplier is unrated (DQ-06).</summary>
    public decimal? Rating { get; set; }

    /// <summary>Whether the supplier can ship by mail order.</summary>
    public bool CanMailOrder { get; set; }

    /// <summary>When the seeder last wrote this row (set by OR-005).</summary>
    public DateTimeOffset SeededAt { get; set; }

    /// <summary>Categories the supplier handles (part of the supplier aggregate).</summary>
    public List<SupplierCategoryRow> Categories { get; set; } = [];

    /// <summary>ZIP ranges the supplier serves (part of the supplier aggregate).</summary>
    public List<SupplierServiceAreaRow> ServiceAreas { get; set; } = [];
}

namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>Persistence shape of the <c>supplier_service_areas</c> table: one inclusive ZIP range a supplier serves.</summary>
public sealed class SupplierServiceAreaRow
{
    /// <summary>Owning supplier (part of the composite primary key).</summary>
    public required string SupplierId { get; set; }

    /// <summary>First ZIP of the range, 0 to 99999 (part of the composite primary key).</summary>
    public int StartZip { get; set; }

    /// <summary>Last ZIP of the range, inclusive, not lower than <see cref="StartZip"/>.</summary>
    public int EndZip { get; set; }

    /// <summary>Owning supplier.</summary>
    public SupplierRow? Supplier { get; set; }
}

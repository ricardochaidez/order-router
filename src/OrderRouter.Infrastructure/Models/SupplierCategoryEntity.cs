namespace OrderRouter.Infrastructure.Models;

/// <summary>Persistence shape of the <c>supplier_categories</c> table: one category a supplier handles.</summary>
public sealed class SupplierCategoryEntity
{
    /// <summary>Owning supplier (part of the composite primary key).</summary>
    public required string SupplierId { get; set; }

    /// <summary>Category value (part of the composite primary key); joins <c>products.category</c> by value.</summary>
    public required string Category { get; set; }

    /// <summary>Owning supplier.</summary>
    public SupplierEntity? Supplier { get; set; }
}

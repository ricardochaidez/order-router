using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>Maps <see cref="SupplierCategoryRow"/> to <c>supplier_categories</c> (ADR-0002).</summary>
internal sealed class SupplierCategoryRowConfiguration : IEntityTypeConfiguration<SupplierCategoryRow>
{
    public void Configure(EntityTypeBuilder<SupplierCategoryRow> builder)
    {
        builder.ToTable("supplier_categories");
        builder.HasKey(row => new { row.SupplierId, row.Category });
        builder.Property(row => row.SupplierId).HasColumnName("supplier_id");
        builder.Property(row => row.Category).HasColumnName("category");

        // Serves "which suppliers handle category X"; the PK's leading supplier_id serves the FK lookups.
        builder.HasIndex(row => new { row.Category, row.SupplierId })
            .HasDatabaseName("ix_supplier_categories_category_supplier_id");
    }
}

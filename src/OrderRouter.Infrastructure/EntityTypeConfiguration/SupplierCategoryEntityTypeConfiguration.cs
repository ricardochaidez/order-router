using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.EntityTypeConfiguration;

/// <summary>Maps <see cref="SupplierCategoryEntity"/> to <c>supplier_categories</c> (ADR-0002).</summary>
internal sealed class SupplierCategoryEntityTypeConfiguration : IEntityTypeConfiguration<SupplierCategoryEntity>
{
    public void Configure(EntityTypeBuilder<SupplierCategoryEntity> builder)
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

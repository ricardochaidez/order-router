using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.EntityTypeConfiguration;

/// <summary>Maps <see cref="SupplierServiceAreaEntity"/> to <c>supplier_service_areas</c> (ADR-0002).</summary>
internal sealed class SupplierServiceAreaEntityTypeConfiguration : IEntityTypeConfiguration<SupplierServiceAreaEntity>
{
    public void Configure(EntityTypeBuilder<SupplierServiceAreaEntity> builder)
    {
        // Check constraints mirror the ZipCode / ZipRange invariants (defence in depth).
        builder.ToTable("supplier_service_areas", table =>
        {
            table.HasCheckConstraint("ck_supplier_service_areas_zip_bounds", "start_zip BETWEEN 0 AND 99999 AND end_zip BETWEEN 0 AND 99999");
            table.HasCheckConstraint("ck_supplier_service_areas_start_le_end", "start_zip <= end_zip");
        });

        builder.HasKey(row => new { row.SupplierId, row.StartZip });
        builder.Property(row => row.SupplierId).HasColumnName("supplier_id");
        builder.Property(row => row.StartZip).HasColumnName("start_zip");
        builder.Property(row => row.EndZip).HasColumnName("end_zip");

        // B-tree for ~5,500 segments (ADR-0002); int4range + GiST is the upgrade path, not needed now.
        builder.HasIndex(row => new { row.StartZip, row.EndZip })
            .HasDatabaseName("ix_supplier_service_areas_start_zip_end_zip");
    }
}

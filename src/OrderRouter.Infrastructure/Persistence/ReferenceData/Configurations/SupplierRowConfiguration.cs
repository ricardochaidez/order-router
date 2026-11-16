using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData.Configurations;

/// <summary>Maps <see cref="SupplierRow"/> to <c>suppliers</c> with explicit snake_case names (ADR-0002).</summary>
internal sealed class SupplierRowConfiguration : IEntityTypeConfiguration<SupplierRow>
{
    public void Configure(EntityTypeBuilder<SupplierRow> builder)
    {
        builder.ToTable("suppliers", table =>
        {
            // Mirrors the Rating invariant (defence in depth); NULL means unrated.
            table.HasCheckConstraint("ck_suppliers_rating", "rating IS NULL OR rating BETWEEN 1 AND 10");
        });

        builder.HasKey(row => row.SupplierId);
        builder.Property(row => row.SupplierId).HasColumnName("supplier_id");
        builder.Property(row => row.Name).HasColumnName("name");

        // numeric(4,2) holds 1.00-10.00 exactly; the domain already rounded to 2 places,
        // so the database must never round again (numeric(3,1) would turn 6.75 into 6.8).
        builder.Property(row => row.Rating).HasColumnName("rating").HasColumnType("numeric(4,2)");
        builder.Property(row => row.CanMailOrder).HasColumnName("can_mail_order");
        builder.Property(row => row.SeededAt).HasColumnName("seeded_at");

        // Categories and service areas belong to the supplier aggregate, so they cascade with it.
        builder.HasMany(row => row.Categories)
            .WithOne(child => child.Supplier)
            .HasForeignKey(child => child.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(row => row.ServiceAreas)
            .WithOne(child => child.Supplier)
            .HasForeignKey(child => child.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

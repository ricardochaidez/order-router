using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData.Configurations;

/// <summary>Maps <see cref="ProductRow"/> to <c>products</c> (ADR-0002).</summary>
internal sealed class ProductRowConfiguration : IEntityTypeConfiguration<ProductRow>
{
    public void Configure(EntityTypeBuilder<ProductRow> builder)
    {
        builder.ToTable("products");
        builder.HasKey(row => row.ProductCode);
        builder.Property(row => row.ProductCode).HasColumnName("product_code");
        builder.Property(row => row.Name).HasColumnName("name");
        builder.Property(row => row.Category).HasColumnName("category");
    }
}

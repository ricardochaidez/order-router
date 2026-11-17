using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.EntityTypeConfiguration;

/// <summary>Maps <see cref="ProductEntity"/> to <c>products</c> (ADR-0002).</summary>
internal sealed class ProductEntityTypeConfiguration : IEntityTypeConfiguration<ProductEntity>
{
    public void Configure(EntityTypeBuilder<ProductEntity> builder)
    {
        builder.ToTable("products");
        builder.HasKey(row => row.ProductCode);
        builder.Property(row => row.ProductCode).HasColumnName("product_code");
        builder.Property(row => row.Name).HasColumnName("name");
        builder.Property(row => row.Category).HasColumnName("category");
    }
}

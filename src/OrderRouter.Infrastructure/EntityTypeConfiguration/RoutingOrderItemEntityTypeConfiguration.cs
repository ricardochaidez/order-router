using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.EntityTypeConfiguration;

/// <summary>
/// Maps <see cref="RoutingOrderItemEntity"/> to <c>routing_order_items</c> (ADR-0007). There is no foreign key to
/// <c>suppliers</c>: a stored order is a historical record (AC3).
/// </summary>
internal sealed class RoutingOrderItemEntityTypeConfiguration : IEntityTypeConfiguration<RoutingOrderItemEntity>
{
    public void Configure(EntityTypeBuilder<RoutingOrderItemEntity> builder)
    {
        builder.ToTable("routing_order_items", table =>
        {
            table.HasCheckConstraint("ck_routing_order_items_line_number", "line_number >= 1");
            table.HasCheckConstraint("ck_routing_order_items_quantity", "quantity >= 1");
            table.HasCheckConstraint("ck_routing_order_items_fulfillment_mode", "fulfillment_mode IS NULL OR fulfillment_mode IN ('local', 'mail')");
        });

        builder.HasKey(row => new { row.RoutingOrderId, row.LineNumber });
        builder.Property(row => row.RoutingOrderId).HasColumnName("routing_order_id");
        builder.Property(row => row.LineNumber).HasColumnName("line_number");
        builder.Property(row => row.ProductCode).HasColumnName("product_code");
        builder.Property(row => row.Quantity).HasColumnName("quantity");
        builder.Property(row => row.Category).HasColumnName("category");
        builder.Property(row => row.SupplierId).HasColumnName("supplier_id");
        builder.Property(row => row.SupplierName).HasColumnName("supplier_name");
        builder.Property(row => row.FulfillmentMode).HasColumnName("fulfillment_mode");
        builder.Property(row => row.ReasonCode).HasColumnName("reason_code");
        builder.Property(row => row.Message).HasColumnName("message");
    }
}

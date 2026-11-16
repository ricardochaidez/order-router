using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderRouter.Infrastructure.Persistence.RoutingOrders.Configurations;

/// <summary>Maps <see cref="RoutingOrderWarningRow"/> to <c>routing_order_warnings</c> (ADR-0007).</summary>
internal sealed class RoutingOrderWarningRowConfiguration : IEntityTypeConfiguration<RoutingOrderWarningRow>
{
    public void Configure(EntityTypeBuilder<RoutingOrderWarningRow> builder)
    {
        builder.ToTable("routing_order_warnings", table =>
        {
            table.HasCheckConstraint("ck_routing_order_warnings_ordinal", "ordinal >= 1");
        });

        builder.HasKey(row => new { row.RoutingOrderId, row.Ordinal });
        builder.Property(row => row.RoutingOrderId).HasColumnName("routing_order_id");
        builder.Property(row => row.Ordinal).HasColumnName("ordinal");
        builder.Property(row => row.Code).HasColumnName("code");
        builder.Property(row => row.Message).HasColumnName("message");
        builder.Property(row => row.UnfulfilledProductCodes).HasColumnName("unfulfilled_product_codes").HasColumnType("text[]");
        builder.Property(row => row.AffectedProductCodes).HasColumnName("affected_product_codes").HasColumnType("text[]");
    }
}

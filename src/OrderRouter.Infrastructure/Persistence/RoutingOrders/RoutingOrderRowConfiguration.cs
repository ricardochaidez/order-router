using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderRouter.Infrastructure.Persistence.RoutingOrders;

/// <summary>Maps <see cref="RoutingOrderRow"/> to <c>routing_orders</c> with explicit snake_case names (ADR-0007).</summary>
internal sealed class RoutingOrderRowConfiguration : IEntityTypeConfiguration<RoutingOrderRow>
{
    public void Configure(EntityTypeBuilder<RoutingOrderRow> builder)
    {
        builder.ToTable("routing_orders", table =>
        {
            // Defence in depth: the application writes these values, the database refuses anything else.
            table.HasCheckConstraint("ck_routing_orders_status", "status IN ('routed', 'partially_routed', 'not_routed')");
            table.HasCheckConstraint("ck_routing_orders_search_mode", "search_mode IS NULL OR search_mode IN ('exact', 'greedy')");
        });

        builder.HasKey(row => row.Id);

        // The server generates the UUIDv7 (OR-027); the database must never invent one.
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.OrderId).HasColumnName("order_id");
        builder.Property(row => row.CallerHash).HasColumnName("caller_hash");
        builder.Property(row => row.CustomerZip).HasColumnName("customer_zip").HasColumnType("char(5)");
        builder.Property(row => row.MailOrder).HasColumnName("mail_order");
        builder.Property(row => row.Status).HasColumnName("status");
        builder.Property(row => row.Feasible).HasColumnName("feasible");
        builder.Property(row => row.ExperimentArms).HasColumnName("experiment_arms").HasColumnType("jsonb");
        builder.Property(row => row.ConfigVersion).HasColumnName("config_version");
        builder.Property(row => row.SearchMode).HasColumnName("search_mode");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");

        // Support look-ups by the caller's order id. Not unique on purpose (SC-29, AC2).
        builder.HasIndex(row => row.OrderId).HasDatabaseName("ix_routing_orders_order_id");

        // Items and warnings are part of the stored order, so they cascade with it.
        builder.HasMany(row => row.Items)
            .WithOne()
            .HasForeignKey(item => item.RoutingOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(row => row.Warnings)
            .WithOne()
            .HasForeignKey(warning => warning.RoutingOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

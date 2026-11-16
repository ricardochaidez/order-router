using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Persistence.ReferenceData;
using OrderRouter.Infrastructure.Persistence.RoutingOrders;

namespace OrderRouter.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for reference data now and stored orders later (ADR-0007).
/// Reads go through repositories (OR-006) that use <c>AsNoTracking</c> and project into domain objects.
/// </summary>
/// <param name="options">Context options, normally configured by <c>AddOrderRouterDatabase</c>.</param>
public sealed class OrderRouterDbContext(DbContextOptions<OrderRouterDbContext> options) : DbContext(options)
{
    /// <summary>Rows of <c>suppliers</c>.</summary>
    public DbSet<SupplierRow> Suppliers => Set<SupplierRow>();

    /// <summary>Rows of <c>supplier_categories</c>.</summary>
    public DbSet<SupplierCategoryRow> SupplierCategories => Set<SupplierCategoryRow>();

    /// <summary>Rows of <c>supplier_service_areas</c>.</summary>
    public DbSet<SupplierServiceAreaRow> SupplierServiceAreas => Set<SupplierServiceAreaRow>();

    /// <summary>Rows of <c>products</c>.</summary>
    public DbSet<ProductRow> Products => Set<ProductRow>();

    /// <summary>Rows of <c>routing_orders</c> (stored routing decisions, ADR-0007).</summary>
    public DbSet<RoutingOrderRow> RoutingOrders => Set<RoutingOrderRow>();

    /// <summary>Rows of <c>routing_order_items</c>.</summary>
    public DbSet<RoutingOrderItemRow> RoutingOrderItems => Set<RoutingOrderItemRow>();

    /// <summary>Rows of <c>routing_order_warnings</c>.</summary>
    public DbSet<RoutingOrderWarningRow> RoutingOrderWarnings => Set<RoutingOrderWarningRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderRouterDbContext).Assembly);
}

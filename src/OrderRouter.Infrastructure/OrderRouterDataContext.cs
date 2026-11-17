using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Interfaces;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure;

/// <summary>
/// The single EF Core context for reference data now and stored orders later (ADR-0007).
/// Reads go through repositories (OR-006) that use <c>AsNoTracking</c> and project into domain objects.
/// </summary>
/// <param name="options">Context options, normally configured by <c>AddOrderRouterDatabase</c>.</param>
public sealed class OrderRouterDataContext(DbContextOptions<OrderRouterDataContext> options) : DbContext(options), IOrderRouterDataContext
{
    /// <summary>Rows of <c>suppliers</c>.</summary>
    public DbSet<SupplierEntity> Suppliers => Set<SupplierEntity>();

    /// <summary>Rows of <c>supplier_categories</c>.</summary>
    public DbSet<SupplierCategoryEntity> SupplierCategories => Set<SupplierCategoryEntity>();

    /// <summary>Rows of <c>supplier_service_areas</c>.</summary>
    public DbSet<SupplierServiceAreaEntity> SupplierServiceAreas => Set<SupplierServiceAreaEntity>();

    /// <summary>Rows of <c>products</c>.</summary>
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    /// <summary>Rows of <c>routing_orders</c> (stored routing decisions, ADR-0007).</summary>
    public DbSet<RoutingOrderEntity> RoutingOrders => Set<RoutingOrderEntity>();

    /// <summary>Rows of <c>routing_order_items</c>.</summary>
    public DbSet<RoutingOrderItemEntity> RoutingOrderItems => Set<RoutingOrderItemEntity>();

    /// <summary>Rows of <c>routing_order_warnings</c>.</summary>
    public DbSet<RoutingOrderWarningEntity> RoutingOrderWarnings => Set<RoutingOrderWarningEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderRouterDataContext).Assembly);
}

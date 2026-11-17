using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.Interfaces;

/// <summary>
/// The testable face of <see cref="OrderRouterDataContext"/>: the tables providers read and write, and the save.
/// Providers depend on this interface, not on the concrete context.
/// </summary>
public interface IOrderRouterDataContext
{
    /// <summary>Rows of <c>suppliers</c>.</summary>
    DbSet<SupplierEntity> Suppliers { get; }

    /// <summary>Rows of <c>supplier_categories</c>.</summary>
    DbSet<SupplierCategoryEntity> SupplierCategories { get; }

    /// <summary>Rows of <c>supplier_service_areas</c>.</summary>
    DbSet<SupplierServiceAreaEntity> SupplierServiceAreas { get; }

    /// <summary>Rows of <c>products</c>.</summary>
    DbSet<ProductEntity> Products { get; }

    /// <summary>Rows of <c>routing_orders</c> (stored routing decisions, ADR-0007).</summary>
    DbSet<RoutingOrderEntity> RoutingOrders { get; }

    /// <summary>Rows of <c>routing_order_items</c>.</summary>
    DbSet<RoutingOrderItemEntity> RoutingOrderItems { get; }

    /// <summary>Rows of <c>routing_order_warnings</c>.</summary>
    DbSet<RoutingOrderWarningEntity> RoutingOrderWarnings { get; }

    /// <summary>The change tracker; a failed save clears it so the scoped context stays clean.</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>Saves all pending changes in one transaction.</summary>
    /// <param name="ct">Cancels the work.</param>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

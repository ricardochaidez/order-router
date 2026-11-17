namespace OrderRouter.Infrastructure.Models;

/// <summary>
/// Persistence shape of <c>routing_order_items</c>: one requested line and what happened to it (ADR-0007).
/// A routed line has the supplier columns; an unfulfillable line has a reason code and message; a line held back
/// by all-or-nothing has neither (the order's status explains why).
/// </summary>
public sealed class RoutingOrderItemEntity
{
    /// <summary>The owning order (part of the primary key).</summary>
    public Guid RoutingOrderId { get; set; }

    /// <summary>Position in the request, 1-based (part of the primary key).</summary>
    public int LineNumber { get; set; }

    /// <summary>The product code as requested.</summary>
    public required string ProductCode { get; set; }

    /// <summary>At least 1.</summary>
    public int Quantity { get; set; }

    /// <summary>The product category; <c>null</c> when the product is unknown.</summary>
    public string? Category { get; set; }

    /// <summary>The chosen supplier. A plain copy, not a foreign key, so a re-seed never changes or blocks a stored order.</summary>
    public string? SupplierId { get; set; }

    /// <summary>The supplier name at routing time (snapshot).</summary>
    public string? SupplierName { get; set; }

    /// <summary><c>local</c> or <c>mail</c>; <c>null</c> when the line was not routed.</summary>
    public string? FulfillmentMode { get; set; }

    /// <summary>Why the line could not be fulfilled; <c>null</c> otherwise.</summary>
    public string? ReasonCode { get; set; }

    /// <summary>Human-readable reason; <c>null</c> unless the line is unfulfillable.</summary>
    public string? Message { get; set; }
}

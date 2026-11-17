namespace OrderRouter.Infrastructure.Models;

/// <summary>Persistence shape of <c>routing_order_warnings</c>: one dependency warning of a stored order (ADR-0007).</summary>
public sealed class RoutingOrderWarningEntity
{
    /// <summary>The owning order (part of the primary key).</summary>
    public Guid RoutingOrderId { get; set; }

    /// <summary>Position in the response, starting at 1 (part of the primary key).</summary>
    public int Ordinal { get; set; }

    /// <summary>Machine-readable warning code.</summary>
    public required string Code { get; set; }

    /// <summary>Human-readable warning.</summary>
    public required string Message { get; set; }

    /// <summary>Products that could not be fulfilled.</summary>
    public string[] UnfulfilledProductCodes { get; set; } = [];

    /// <summary>Routed products affected by the unfulfilled ones.</summary>
    public string[] AffectedProductCodes { get; set; } = [];
}

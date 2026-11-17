
namespace OrderRouter.Domain.Entities.Routing;

/// <summary>
/// One requested line and what happened to it. A routed line has the supplier fields; an unfulfillable line has a
/// reason and message; a line held back by all-or-nothing has neither (the order's status explains why).
/// </summary>
/// <param name="LineNumber">Position in the request, 1-based.</param>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">At least 1.</param>
/// <param name="Category">The product category; <see langword="null"/> for an unknown product.</param>
/// <param name="SupplierId">The chosen supplier; <see langword="null"/> when not routed.</param>
/// <param name="SupplierName">The supplier name at routing time.</param>
/// <param name="FulfillmentMode"><c>local</c> or <c>mail</c>; <see langword="null"/> when not routed.</param>
/// <param name="ReasonCode">Why the line is unfulfillable; <see langword="null"/> otherwise.</param>
/// <param name="Message">The human-readable reason; <see langword="null"/> otherwise.</param>
public sealed record StoredOrderItem(
    int LineNumber,
    string ProductCode,
    int Quantity,
    string? Category,
    string? SupplierId,
    string? SupplierName,
    string? FulfillmentMode,
    string? ReasonCode,
    string? Message);

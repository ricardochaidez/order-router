namespace OrderRouter.Application.Routing.Contracts;

/// <summary>A line item that could not be routed, with a machine-readable reason so the caller can act (FR-14).</summary>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">The requested quantity.</param>
/// <param name="Category">The category, or <c>null</c> when the product is unknown.</param>
/// <param name="ReasonCode">A stable code such as <c>UNKNOWN_PRODUCT</c>.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record UnfulfilledItem(string ProductCode, int Quantity, string? Category, string ReasonCode, string Message);

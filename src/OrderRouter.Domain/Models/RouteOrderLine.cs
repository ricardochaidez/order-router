
namespace OrderRouter.Domain.Models;

/// <summary>One requested line item of a validated order.</summary>
/// <param name="ProductCode">The product code exactly as requested (whitespace and case are handled at lookup, FR-05).</param>
/// <param name="Quantity">At least 1.</param>
public sealed record RouteOrderLine(string ProductCode, int Quantity);

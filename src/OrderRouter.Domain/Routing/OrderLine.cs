using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing;

/// <summary>One requested line, after the product lookup.</summary>
/// <param name="ProductCode">The product code as the caller sent it.</param>
/// <param name="Quantity">Ordered quantity; never split across suppliers (FR-10).</param>
/// <param name="Category">The product's category, or <see langword="null"/> when the product is unknown (FR-05).</param>
public sealed record OrderLine(string ProductCode, int Quantity, Category? Category);

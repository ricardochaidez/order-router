using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>One order line assigned to a supplier (FR-15, FR-16).</summary>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">The requested quantity.</param>
/// <param name="Category">The product's category.</param>
/// <param name="Supplier">The supplier that will ship the line.</param>
/// <param name="Mode">Local delivery or mail (FR-09).</param>
public sealed record RoutedLine(string ProductCode, int Quantity, Category Category, Supplier Supplier, FulfillmentMode Mode);

namespace OrderRouter.Application.Routing.Contracts;

/// <summary>One line item assigned to a supplier (FR-15, FR-16).</summary>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">The requested quantity. Never split across suppliers (FR-10).</param>
/// <param name="Category">The category from the normalized product data.</param>
/// <param name="FulfillmentMode"><c>local</c> or <c>mail</c> (FR-09).</param>
public sealed record RoutedItem(string ProductCode, int Quantity, string Category, string FulfillmentMode);

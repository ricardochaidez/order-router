using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>An order line that could not be routed (mirrors the v1 contract; the Domain cannot reference the Application).</summary>
/// <param name="ProductCode">Product code as the caller sent it.</param>
/// <param name="Quantity">Ordered quantity.</param>
/// <param name="Category">Category, when the product is known.</param>
/// <param name="ReasonCode">Wire reason code, for example <c>NO_SUPPLIER_FOR_CATEGORY</c>.</param>
public sealed record UnfulfilledLine(string ProductCode, int Quantity, Category? Category, string ReasonCode)
{
    /// <summary>Reason code for a product code that is not in the catalog (FR-05, requirements §9.2).</summary>
    public const string UnknownProductCode = "UNKNOWN_PRODUCT";
}

using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Helpers;

/// <summary>
/// The human-readable reason of an unfulfilled line (FR-16). The text is stored with the order, so a stored order
/// reads back exactly as it was returned (ADR-0007).
/// </summary>
public static class UnfulfilledMessages
{
    /// <summary>Returns the message for the line's reason code.</summary>
    /// <param name="line">The unfulfilled line.</param>
    /// <exception cref="ArgumentOutOfRangeException">The reason code is not one this helper knows (fail safe: no guessed text).</exception>
    public static string For(UnfulfilledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.ReasonCode switch
        {
            UnfulfilledLine.UnknownProductCode => $"Product code '{line.ProductCode}' was not found in the catalog.",
            "NO_SUPPLIER_FOR_CATEGORY_IN_AREA" => $"No supplier delivers '{line.Category}' to the delivery ZIP.",
            "NO_SUPPLIER_FOR_CATEGORY" => $"No mail-order supplier handles '{line.Category}'.",
            UnfulfilledLine.TooManyCategories =>
                $"The order spans more than {PlanSearchRequest.MaxCategories} product categories, so it was not routed. Review or split the order.",
            _ => throw new ArgumentOutOfRangeException(nameof(line), line.ReasonCode, "Unknown unfulfilled reason code."),
        };
    }
}

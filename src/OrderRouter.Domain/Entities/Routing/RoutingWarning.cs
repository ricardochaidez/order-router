
namespace OrderRouter.Domain.Entities.Routing;

/// <summary>A dependency warning on a partial route (FR-18).</summary>
/// <param name="Code">A stable warning code.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="UnfulfilledProductCodes">Product codes that were not routed.</param>
/// <param name="AffectedProductCodes">Routed product codes that depend on the unfulfilled ones.</param>
public sealed record RoutingWarning(
    string Code,
    string Message,
    IReadOnlyList<string> UnfulfilledProductCodes,
    IReadOnlyList<string> AffectedProductCodes);

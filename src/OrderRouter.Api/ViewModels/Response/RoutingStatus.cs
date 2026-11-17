
namespace OrderRouter.Api.ViewModels.Response;

/// <summary>
/// The overall outcome of a routing request. Serialized as a snake_case string (ADR-0004).
/// New values may be added within v1, so clients must tolerate unknown values.
/// </summary>
public enum RoutingStatus
{
    /// <summary>Every item was assigned to a supplier.</summary>
    Routed,

    /// <summary>Some items were assigned and some were not (EXP-3 treatment only).</summary>
    PartiallyRouted,

    /// <summary>Nothing was routed: no supplier, unknown product, EXP-3 control, or an invalid request.</summary>
    NotRouted,
}

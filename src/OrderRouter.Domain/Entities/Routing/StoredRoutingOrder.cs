
namespace OrderRouter.Domain.Entities.Routing;

/// <summary>
/// A routing decision as it is stored (ADR-0007, FR-20): the request, the decision and its evidence. Everything is
/// copied at routing time (supplier names included), so a later re-seed cannot change it. Immutable. Text values use
/// the same snake_case words as the API (<c>partially_routed</c>, <c>local</c>), so a stored order reads back as it was returned.
/// </summary>
/// <param name="Id">Server-generated UUIDv7.</param>
/// <param name="OrderId">The caller's order id. Not unique (SC-29).</param>
/// <param name="CallerHash">HMAC hash of the caller ID, or <see langword="null"/> when the token had no caller claim (such an order can never be read back).</param>
/// <param name="CustomerZip">The delivery ZIP, 5 digits.</param>
/// <param name="MailOrder">Whether only mail-capable suppliers were allowed.</param>
/// <param name="Status"><c>routed</c>, <c>partially_routed</c> or <c>not_routed</c>.</param>
/// <param name="Feasible">True only when every line was routed.</param>
/// <param name="ExperimentArmsJson">JSON object with the arm and reason of each experiment (AB-04).</param>
/// <param name="ConfigVersion">The experiment configuration version in force.</param>
/// <param name="SearchMode"><c>exact</c> or <c>greedy</c>; <see langword="null"/> when no search ran.</param>
/// <param name="CreatedAt">When the order was stored.</param>
/// <param name="Items">One item per requested line, in request order.</param>
/// <param name="Warnings">Dependency warnings returned with the decision.</param>
public sealed record StoredRoutingOrder(
    Guid Id,
    string OrderId,
    string? CallerHash,
    string CustomerZip,
    bool MailOrder,
    string Status,
    bool Feasible,
    string ExperimentArmsJson,
    string ConfigVersion,
    string? SearchMode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StoredOrderItem> Items,
    IReadOnlyList<RoutingWarning> Warnings);

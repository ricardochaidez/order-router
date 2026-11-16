using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Application.Routing.Storage;

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

/// <summary>
/// One requested line and what happened to it. A routed line has the supplier fields; an unfulfillable line has a
/// reason and message; a line held back by all-or-nothing has neither (the order's status explains why).
/// </summary>
/// <param name="LineNumber">Position in the request, 1-based.</param>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">At least 1.</param>
/// <param name="Category">The product category; <see langword="null"/> for an unknown product.</param>
/// <param name="SupplierId">The chosen supplier; <see langword="null"/> when not routed.</param>
/// <param name="SupplierName">The supplier name at routing time.</param>
/// <param name="FulfillmentMode"><c>local</c> or <c>mail</c>; <see langword="null"/> when not routed.</param>
/// <param name="ReasonCode">Why the line is unfulfillable; <see langword="null"/> otherwise.</param>
/// <param name="Message">The human-readable reason; <see langword="null"/> otherwise.</param>
public sealed record StoredOrderItem(
    int LineNumber,
    string ProductCode,
    int Quantity,
    string? Category,
    string? SupplierId,
    string? SupplierName,
    string? FulfillmentMode,
    string? ReasonCode,
    string? Message);

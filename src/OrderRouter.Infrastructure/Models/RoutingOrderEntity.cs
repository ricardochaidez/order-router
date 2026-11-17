namespace OrderRouter.Infrastructure.Models;

/// <summary>
/// Persistence shape of <c>routing_orders</c>: one stored routing decision (ADR-0007, FR-20). This is <b>not</b> a
/// domain object. It is a historical record, so it holds copies of what was decided and has no link to reference data.
/// </summary>
public sealed class RoutingOrderEntity
{
    /// <summary>Server-generated UUIDv7 (primary key). The server sets it, never the database or the caller (OR-027).</summary>
    public Guid Id { get; set; }

    /// <summary>The caller's order id. Deliberately not unique: every POST creates a new record (SC-29).</summary>
    public required string OrderId { get; set; }

    /// <summary>
    /// HMAC-SHA256 of the caller ID (SC-31), or <c>null</c> when the token had no caller claim. A null hash never
    /// matches a <c>GET</c>, so such an order cannot be read back (fail safe, ADR-0007).
    /// </summary>
    public string? CallerHash { get; set; }

    /// <summary>The delivery ZIP, exactly 5 digits.</summary>
    public required string CustomerZip { get; set; }

    /// <summary>Whether the request allowed only mail-capable suppliers (FR-08).</summary>
    public bool MailOrder { get; set; }

    /// <summary><c>routed</c>, <c>partially_routed</c> or <c>not_routed</c>.</summary>
    public required string Status { get; set; }

    /// <summary>True only when every line was routed (derived from <see cref="Status"/>, stored for querying).</summary>
    public bool Feasible { get; set; }

    /// <summary>JSON with the arm and reason per experiment (AB-04).</summary>
    public required string ExperimentArms { get; set; }

    /// <summary>The experiment configuration version in force when the order was routed.</summary>
    public required string ConfigVersion { get; set; }

    /// <summary><c>exact</c> or <c>greedy</c>; <c>null</c> when no search ran (no known product).</summary>
    public string? SearchMode { get; set; }

    /// <summary>When the order was stored.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>One row per requested line (part of the stored order).</summary>
    public List<RoutingOrderItemEntity> Items { get; set; } = [];

    /// <summary>Dependency warnings returned with the decision.</summary>
    public List<RoutingOrderWarningEntity> Warnings { get; set; } = [];
}

using System.Text.Json.Serialization;

namespace OrderRouter.Application.Routing.Contracts;

/// <summary>One line the caller requested, as stored (FR-21).</summary>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">The requested quantity.</param>
public sealed record RequestedItem(string ProductCode, int Quantity);

/// <summary>
/// The body of <c>GET /api/v1/route/{id}</c> (FR-21, requirements §9.5): the stored request, its items and the
/// decision exactly as POST returned it. Reading never routes again.
/// </summary>
/// <remarks>
/// It keeps the invariants of <see cref="RouteResponse"/>: <see cref="Feasible"/> is derived from
/// <see cref="Status"/>, and the collections are never <c>null</c>.
/// </remarks>
public sealed record StoredOrderResponse
{
    /// <summary>The stored order id (UUIDv7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The caller's order id, as sent.</summary>
    public required string OrderId { get; init; }

    /// <summary>The delivery ZIP, 5 digits.</summary>
    public required string CustomerZip { get; init; }

    /// <summary>Whether only mail-capable suppliers were allowed.</summary>
    public required bool MailOrder { get; init; }

    /// <summary>When the order was stored (UTC).</summary>
    [JsonConverter(typeof(UtcTimestampJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Every requested line in request order, including lines that were not routed. Always present.</summary>
    public IReadOnlyList<RequestedItem> Items
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary><c>true</c> only when every item was routed. Derived from <see cref="Status"/>.</summary>
    public bool Feasible => Status == RoutingStatus.Routed;

    /// <summary>The overall outcome.</summary>
    public required RoutingStatus Status { get; init; }

    /// <summary>Items grouped by supplier, as POST returned them. Always present.</summary>
    public IReadOnlyList<SupplierRouting> Routing
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>Items that could not be routed. Always present.</summary>
    public IReadOnlyList<UnfulfilledItem> UnfulfilledItems
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>Dependency warnings. Always present.</summary>
    public IReadOnlyList<RoutingWarning> Warnings
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>One message per unfulfilled item. Always present.</summary>
    public IReadOnlyList<string> Errors
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>
    /// Optional evidence of how the order was routed: the experiment arms and the search mode. Present only when
    /// <c>Routing:ExposeDiagnostics</c> is on, and omitted otherwise. Clients must not depend on it (AB-05, OQ-08).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Diagnostics? Diagnostics { get; init; }
}

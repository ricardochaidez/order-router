using System.Text.Json.Serialization;

namespace OrderRouter.Api.ViewModels.Response;

/// <summary>
/// The v1 response envelope, used for both success and 400 responses (SC-15, ADR-0004, requirements §9.2).
/// </summary>
/// <remarks>
/// Two invariants live here, in one place:
/// <list type="bullet">
/// <item><see cref="Feasible"/> is computed from <see cref="Status"/> and can never be set separately, so a partial
/// route can't be mistaken for success (DEV-02).</item>
/// <item>The four collections are never <c>null</c>, even if a caller assigns <c>null</c> (AC9).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var body = RouteResponseViewModel.Invalid(["customer_zip is required."]);
/// // body.Feasible == false, body.Status == RoutingStatus.NotRouted
/// </code>
/// </example>
public sealed record RouteResponseViewModel
{
    /// <summary>Stored order id (UUIDv7, FR-20). Always present on 201 and omitted on 400.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Id { get; init; }

    /// <summary>Echo of the caller's <c>order_id</c>. Omitted on 400.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrderId { get; init; }

    /// <summary><c>true</c> only when every item is routed. Derived from <see cref="Status"/> (ADR-0004).</summary>
    public bool Feasible => Status == RoutingStatus.Routed;

    /// <summary>The overall outcome.</summary>
    public required RoutingStatus Status { get; init; }

    /// <summary>Items grouped by supplier. Always present, empty when nothing is routed.</summary>
    public IReadOnlyList<SupplierRoutingResponseViewModel> Routing
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>Items that could not be routed. Always present.</summary>
    public IReadOnlyList<UnfulfilledItemResponseViewModel> UnfulfilledItems
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>Dependency warnings. Always present.</summary>
    public IReadOnlyList<RoutingWarningResponseViewModel> Warnings
    {
        get => field;
        init => field = value ?? [];
    } = [];

    /// <summary>Validation errors and one message per unfulfilled item. Always present.</summary>
    public IReadOnlyList<string> Errors
    {
        get => field;
        init => field = value ?? [];
    } = [];


    /// <summary>Builds the 400 body: <c>not_routed</c> with every validation error (SC-15).</summary>
    /// <param name="errors">Every validation error, in a stable order.</param>
    public static RouteResponseViewModel Invalid(IReadOnlyList<string> errors)
        => new() { Status = RoutingStatus.NotRouted, Errors = errors };
}

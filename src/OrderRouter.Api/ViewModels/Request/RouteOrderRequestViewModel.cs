using System.ComponentModel.DataAnnotations;
using OrderRouter.Api.Filters;

namespace OrderRouter.Api.ViewModels.Request;

/// <summary>
/// The wire request for <c>POST /api/v1/route</c> and its alias (requirements §9.1). Every property is nullable on
/// purpose: a missing field must be reported as a validation error, not silently defaulted (FR-03). Unknown JSON
/// fields such as <c>priority</c> or <c>notes</c> are ignored (DQ-15). Error messages are written as the part after
/// the JSON path, because <see cref="ValidationErrorFormatter"/> prepends the snake_case path.
/// </summary>
/// <example>
/// <code>
/// { "order_id": "ORD-EXAMPLE", "customer_zip": "10015", "mail_order": false,
///   "items": [ { "product_code": "WC-STD-001", "quantity": 1 } ] }
/// </code>
/// </example>
public sealed record RouteOrderRequestViewModel
{
    /// <summary>The caller's order id. Required and not blank.</summary>
    [Required(ErrorMessage = "is required and must not be blank.")]
    public string? OrderId { get; init; }

    /// <summary>The delivery ZIP: exactly 5 ASCII digits. <c>\z</c> (not <c>$</c>) so a trailing newline is rejected.</summary>
    [Required(ErrorMessage = "is required.")]
    [RegularExpression(@"^[0-9]{5}\z", ErrorMessage = "must be exactly 5 digits.")]
    public string? CustomerZip { get; init; }

    /// <summary>Optional. Defaults to <c>false</c>, the stricter mode (FR-08).</summary>
    public bool MailOrder { get; init; }

    /// <summary>The requested line items. Required with at least one entry.</summary>
    [Required(ErrorMessage = "is required.")]
    [MinLength(1, ErrorMessage = "must contain at least one item.")]
    [NoNullElements(ErrorMessage = "must not contain null entries.")]
    public List<RouteOrderItemRequestViewModel?>? Items { get; init; }
}

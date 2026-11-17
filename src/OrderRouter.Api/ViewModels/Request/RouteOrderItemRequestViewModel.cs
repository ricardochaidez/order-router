using System.ComponentModel.DataAnnotations;

namespace OrderRouter.Api.ViewModels.Request;

/// <summary>One requested line item on the wire.</summary>
public sealed record RouteOrderItemRequestViewModel
{
    /// <summary>The product code. Required and not blank. Unknown codes are not a validation error (AC7).</summary>
    [Required(ErrorMessage = "is required and must not be blank.")]
    public string? ProductCode { get; init; }

    /// <summary>The quantity: an integer of at least 1.</summary>
    [Required(ErrorMessage = "is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1.")]
    public int? Quantity { get; init; }
}

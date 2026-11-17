using System.ComponentModel.DataAnnotations;

namespace OrderRouter.Api.ViewModels.Request;

/// <summary>One requested line item on the wire.</summary>
public sealed record RouteOrderItemRequestViewModel
{
    /// <summary>
    /// Upper bound on <see cref="ProductCode"/>. A defensive limit, not a catalog rule: the longest seeded code is 17 characters
    /// and HCPCS codes are 5, so 64 leaves wide headroom while keeping every stored and logged row bounded.
    /// </summary>
    public const int MaxProductCodeLength = 64;

    /// <summary>The product code. Required, not blank, at most 64 characters. Unknown codes are not a validation error (AC7).</summary>
    [Required(ErrorMessage = "is required and must not be blank.")]
    [MaxLength(MaxProductCodeLength, ErrorMessage = "must be at most 64 characters.")]
    public string? ProductCode { get; init; }

    /// <summary>The quantity: an integer of at least 1.</summary>
    [Required(ErrorMessage = "is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1.")]
    public int? Quantity { get; init; }
}

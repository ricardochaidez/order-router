namespace OrderRouter.Domain.Eligibility;

/// <summary>Why no supplier can fulfill a category (Technical Requirements §9.4).</summary>
public enum UnfulfillableReason
{
    /// <summary>Mail order is off and no supplier handles the category and serves the ZIP.</summary>
    NoSupplierForCategoryInArea,

    /// <summary>Mail order is on and no mail-capable supplier handles the category.</summary>
    NoSupplierForCategory,
}

/// <summary>Maps <see cref="UnfulfillableReason"/> to its wire code.</summary>
public static class UnfulfillableReasonExtensions
{
    /// <summary>Gets the exact §9.4 <c>reason_code</c> string.</summary>
    /// <param name="reason">The reason.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined reason (fail safe, no guessed code).</exception>
    public static string Code(this UnfulfillableReason reason)
        => reason switch
        {
            UnfulfillableReason.NoSupplierForCategoryInArea => "NO_SUPPLIER_FOR_CATEGORY_IN_AREA",
            UnfulfillableReason.NoSupplierForCategory => "NO_SUPPLIER_FOR_CATEGORY",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown unfulfillable reason."),
        };
}

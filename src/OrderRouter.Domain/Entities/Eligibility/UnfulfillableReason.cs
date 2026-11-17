namespace OrderRouter.Domain.Entities.Eligibility;

/// <summary>Why no supplier can fulfill a category (Technical Requirements §9.4).</summary>
public enum UnfulfillableReason
{
    /// <summary>Mail order is off and no supplier handles the category and serves the ZIP.</summary>
    NoSupplierForCategoryInArea,

    /// <summary>Mail order is on and no mail-capable supplier handles the category.</summary>
    NoSupplierForCategory,
}

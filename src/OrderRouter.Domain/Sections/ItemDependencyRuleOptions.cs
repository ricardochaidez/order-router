namespace OrderRouter.Domain.Sections;

/// <summary>One rule as it is written in configuration.</summary>
public sealed class ItemDependencyRuleOptions
{
    /// <summary>The category whose routed items need the required categories, for example <c>hospital bed</c>.</summary>
    public string? Category { get; set; }

    /// <summary>The required categories, for example <c>[ "patient lift" ]</c>.</summary>
    public List<string?> Requires { get; set; } = [];
}

using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;

namespace OrderRouter.Domain.Sections;

/// <summary>
/// The <c>ItemDependencies</c> configuration section (SC-11, DR-01 to DR-03). The service ships with no rules: the
/// real rule set is clinical content owned by clinical operations, not engineering (DR-03, DR-05). It is read at
/// startup, so a change needs a restart. Validated at startup by <see cref="ItemDependencyOptionsValidator"/>, which
/// checks only the shape and never reads reference data (DR-02).
/// </summary>
/// <example>
/// <code>
/// "ItemDependencies": {
///   "Rules": [
///     { "Category": "hospital bed", "Requires": [ "patient lift" ] }
///   ]
/// }
/// </code>
/// </example>
public sealed class ItemDependencyOptions
{
    /// <summary>Name of the configuration section.</summary>
    public static readonly string Name = "ItemDependencies";

    /// <summary>The configured rules; empty by default.</summary>
    public List<ItemDependencyRuleOptions> Rules { get; set; } = [];

    /// <summary>Converts to validated domain rules, in configured order.</summary>
    /// <exception cref="OrderRouter.Domain.Models.DomainValidationException">A rule is malformed.</exception>
    public IReadOnlyList<ItemDependencyRule> ToRules()
        => [.. Rules.Select(rule => new ItemDependencyRule(new Category(rule.Category), rule.Requires.Select(required => new Category(required))))];
}

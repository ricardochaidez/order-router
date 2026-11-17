using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>
/// A configured item-dependency rule (SC-11, DR-01): items of <see cref="Category"/> are clinically needed together
/// with items of every category in <see cref="Requires"/>. Rules are category-level and only produce warnings on a
/// partial route (FR-18, DR-04); they never change eligibility or ranking. Immutable.
/// </summary>
/// <remarks>
/// A rule links <b>different</b> categories. Lines of one category always go to the same supplier, so a category can
/// never be split from itself, and a rule requiring its own category could never fire (DR-01).
/// </remarks>
/// <example>
/// <code>
/// var rule = new ItemDependencyRule(new Category("hospital bed"), [new Category("patient lift")]);
/// </code>
/// </example>
public sealed class ItemDependencyRule
{
    /// <summary>Creates a rule.</summary>
    /// <param name="category">The category whose routed items need the required categories.</param>
    /// <param name="requires">The required categories; at least one, none equal to <paramref name="category"/>. Duplicates are removed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requires"/> is null.</exception>
    /// <exception cref="DomainValidationException">A category is uninitialized, <paramref name="requires"/> is empty, or it names <paramref name="category"/>.</exception>
    public ItemDependencyRule(Category category, IEnumerable<Category> requires)
    {
        ArgumentNullException.ThrowIfNull(requires);

        var required = requires.Distinct().ToArray();
        var errors = Validate(category, required);

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid item-dependency rule: {string.Join("; ", errors)}.");
        }

        Category = category;
        Requires = required;
    }

    /// <summary>The category whose routed items depend on the required categories.</summary>
    public Category Category { get; }

    /// <summary>The required categories, distinct, in configured order; never empty.</summary>
    public IReadOnlyList<Category> Requires { get; }

    /// <summary>Every category the rule names: <see cref="Category"/> first, then <see cref="Requires"/>.</summary>
    public IEnumerable<Category> Categories => [Category, .. Requires];

    /// <summary>
    /// Returns the warning this rule raises for a decision (FR-18): when at least one line of <see cref="Category"/>
    /// is routed and at least one line of a required category is unfulfilled. Product codes are listed once each, in
    /// request order. An unknown product has no category, so it never matches.
    /// </summary>
    /// <param name="routed">The routed lines of the decision.</param>
    /// <param name="unfulfilled">The unfulfilled lines of the decision.</param>
    /// <returns>The warning, or <see langword="null"/> when the rule does not apply.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <example>
    /// <code>
    /// // Bed routed, lift unfulfilled:
    /// var warning = rule.WarningFor(decision.RoutedLines, decision.Unfulfilled);
    /// // warning.AffectedProductCodes == ["HB-FUL-018"], warning.UnfulfilledProductCodes == ["PL-ELEC-043"]
    /// </code>
    /// </example>
    public RoutingWarning? WarningFor(IReadOnlyList<RoutedLine> routed, IReadOnlyList<UnfulfilledLine> unfulfilled)
    {
        ArgumentNullException.ThrowIfNull(routed);
        ArgumentNullException.ThrowIfNull(unfulfilled);

        var affected = routed
            .Where(line => line.Category == Category)
            .Select(line => (line.ProductCode, Category: line.Category))
            .DistinctBy(line => line.ProductCode, StringComparer.Ordinal)
            .ToArray();
        var missing = unfulfilled
            .Where(line => line.Category is { } category && Requires.Contains(category))
            .Select(line => (line.ProductCode, Category: line.Category!.Value))
            .DistinctBy(line => line.ProductCode, StringComparer.Ordinal)
            .ToArray();

        if (affected.Length == 0 || missing.Length == 0)
        {
            return null;
        }

        var message =
            $"{Describe(affected)} {(affected.Length == 1 ? "is" : "are")} routed, but {(affected.Length == 1 ? "its" : "their")} required " +
            $"{(missing.Length == 1 ? "companion" : "companions")} {Describe(missing)} could not be fulfilled. Review before releasing.";

        return new RoutingWarning(
            RoutingWarning.DependentItemUnfulfilledCode,
            message,
            [.. missing.Select(line => line.ProductCode)],
            [.. affected.Select(line => line.ProductCode)]);
    }

    // "HB-FUL-018 (hospital bed), HB-SEM-017 (hospital bed)", matching the §9 example.
    private static string Describe(IEnumerable<(string ProductCode, Category Category)> lines)
        => string.Join(", ", lines.Select(line => $"{line.ProductCode} ({line.Category})"));

    // default(Category) has no value; reading it would throw, so it is reported as a rule error instead.
    private static List<string> Validate(Category category, Category[] requires)
    {
        var errors = new List<string>();

        if (category == default)
        {
            errors.Add("the category is missing");
        }

        if (requires.Length == 0)
        {
            errors.Add("it must require at least one category");
        }

        if (requires.Any(required => required == default))
        {
            errors.Add("a required category is missing");
        }
        else if (category != default && requires.Contains(category))
        {
            errors.Add($"'{category}' cannot require itself");
        }

        return errors;
    }
}

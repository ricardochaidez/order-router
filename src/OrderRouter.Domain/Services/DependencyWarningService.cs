using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Sections;

namespace OrderRouter.Domain.Services;

/// <summary>
/// Applies the configured item-dependency rules to a routing decision (FR-18, SC-08, OR-020). It runs after the
/// decision is made and only reads it, so rules can never change eligibility or ranking (DR-04, AC6). It does no I/O:
/// the caller passes in which rule categories exist in the reference data, and this service skips and logs the rules
/// that name any other category (DR-02, AC4).
/// </summary>
/// <remarks>
/// The rules are read once from <see cref="ItemDependencyOptions"/>, already checked by
/// <see cref="ItemDependencyOptionsValidator"/> at startup, so the service is an immutable singleton.
/// </remarks>
public sealed partial class DependencyWarningService
{
    private readonly IReadOnlyList<ItemDependencyRule> rules;
    private readonly ILogger<DependencyWarningService> logger;

    /// <summary>Creates the service from the configured rules.</summary>
    /// <param name="options">The <c>ItemDependencies</c> section.</param>
    /// <param name="logger">Logs rules skipped at routing time.</param>
    /// <exception cref="OptionsValidationException">The section is malformed (the startup validator reports it first).</exception>
    public DependencyWarningService(IOptions<ItemDependencyOptions> options, ILogger<DependencyWarningService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        rules = options.Value.ToRules();
        this.logger = logger;
        RuleCategories = [.. rules.SelectMany(rule => rule.Categories).Distinct()];
    }

    /// <summary>Every category named by a rule; empty when no rules are configured.</summary>
    public IReadOnlyList<Category> RuleCategories { get; }

    /// <summary>
    /// Whether the decision can carry dependency warnings at all: only a partial route can (FR-18), and only when rules
    /// are configured (AC2). Callers use it to skip the reference-data lookup on every other request.
    /// </summary>
    /// <param name="decision">The routing decision.</param>
    /// <returns><see langword="true"/> when <see cref="WarningsFor"/> may return warnings.</returns>
    public bool AppliesTo(RoutingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return rules.Count > 0 && decision.Status is RoutingOutcomeStatus.PartiallyRouted;
    }

    /// <summary>Returns the dependency warnings for a decision, one per matching rule, in configured order.</summary>
    /// <param name="decision">The routing decision.</param>
    /// <param name="knownCategories">The rule categories that exist in the reference data.</param>
    /// <returns>The warnings; empty when the decision is not a partial route or no rule matches. Never null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<RoutingWarning> WarningsFor(RoutingDecision decision, IReadOnlySet<Category> knownCategories)
    {
        ArgumentNullException.ThrowIfNull(knownCategories);

        if (!AppliesTo(decision))
        {
            return [];
        }

        var warnings = new List<RoutingWarning>();

        foreach (var rule in rules)
        {
            // DR-02: a rule naming a category that is not in the reference data is skipped, never guessed at.
            var unknown = rule.Categories.Where(category => !knownCategories.Contains(category)).ToArray();

            if (unknown.Length > 0)
            {
                // Category names only: no order id, ZIP or caller (NFR-11).
                LogRuleSkipped(rule.Category.Value, string.Join(", ", unknown.Select(category => category.Value)));
                continue;
            }

            if (rule.WarningFor(decision.RoutedLines, decision.Unfulfilled) is { } warning)
            {
                warnings.Add(warning);
            }
        }

        return warnings;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Item-dependency rule for '{RuleCategory}' was skipped: {UnknownCategories} not found in the reference data.")]
    private partial void LogRuleSkipped(string ruleCategory, string unknownCategories);
}

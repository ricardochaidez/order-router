using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Sections;

/// <summary>
/// Stops the host at startup when a dependency rule is malformed (OR-020 AC3), and reports every problem at once.
/// It checks the shape only: non-empty names, at least one required category, no rule requiring its own category, and
/// one rule per category. It never reads reference data, so an unknown category passes here and is skipped at routing
/// time instead (DR-02, AC4, AC5).
/// </summary>
public sealed class ItemDependencyOptionsValidator : IValidateOptions<ItemDependencyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ItemDependencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var seen = new HashSet<Category>();

        for (var i = 0; i < options.Rules.Count; i++)
        {
            var prefix = $"{ItemDependencyOptions.Name}:{nameof(ItemDependencyOptions.Rules)}:{i}";
            var rule = options.Rules[i];

            // A JSON null element binds to a null rule.
            if (rule is null)
            {
                errors.Add($"{prefix} is empty.");
                continue;
            }

            var hasCategory = Category.TryCreate(rule.Category, out var category);

            if (!hasCategory)
            {
                errors.Add($"{prefix}:{nameof(ItemDependencyRuleOptions.Category)} must not be empty.");
            }
            else if (!seen.Add(category))
            {
                // One rule per category keeps one warning per rule (FR-18); list every required category in that rule.
                errors.Add($"{prefix}: '{category}' already has a rule; put all its required categories in one rule.");
            }

            if (rule.Requires.Count == 0)
            {
                errors.Add($"{prefix}:{nameof(ItemDependencyRuleOptions.Requires)} must name at least one category.");
            }

            for (var j = 0; j < rule.Requires.Count; j++)
            {
                if (!Category.TryCreate(rule.Requires[j], out var required))
                {
                    errors.Add($"{prefix}:{nameof(ItemDependencyRuleOptions.Requires)}:{j} must not be empty.");
                }
                else if (hasCategory && required == category)
                {
                    errors.Add($"{prefix}: '{category}' cannot require itself.");
                }
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

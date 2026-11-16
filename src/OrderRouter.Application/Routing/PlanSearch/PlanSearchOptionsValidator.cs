using Microsoft.Extensions.Options;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Application.Routing.PlanSearch;

/// <summary>
/// Stops the host at startup when the limits are invalid (fail fast). It reuses the domain rules, so the
/// ranges are defined in one place, and reports every problem at once.
/// </summary>
public sealed class PlanSearchOptionsValidator : IValidateOptions<PlanSearchOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PlanSearchOptions options)
    {
        var errors = PlanSearchLimits.Validate(options.MaxExactCategories, options.NodeBudget);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(error => $"{PlanSearchOptions.SectionName}: {error}"));
    }
}

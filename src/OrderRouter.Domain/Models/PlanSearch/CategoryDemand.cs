using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>
/// The demand for one category in an order: lines of the same category are grouped so they are
/// fulfilled together and never split (FR-10, ADR-0003). Quantity is not used.
/// </summary>
public sealed record CategoryDemand
{
    /// <summary>Creates a demand.</summary>
    /// <param name="category">The demanded category.</param>
    /// <param name="lineCount">How many order lines fall in the category; at least 1.</param>
    /// <exception cref="DomainValidationException"><paramref name="lineCount"/> is below 1.</exception>
    public CategoryDemand(Category category, int lineCount)
    {
        if (lineCount < 1)
        {
            throw new DomainValidationException($"Category demand line count must be at least 1 (received {lineCount}).");
        }

        Category = category;
        LineCount = lineCount;
    }

    /// <summary>The demanded category.</summary>
    public Category Category { get; }

    /// <summary>Number of order lines in the category; the weight of the category in plan quality.</summary>
    public int LineCount { get; }
}

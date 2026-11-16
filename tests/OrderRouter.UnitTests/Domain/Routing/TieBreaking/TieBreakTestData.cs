using System.Collections.ObjectModel;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.UnitTests.Domain.Routing.TieBreaking;

internal static class TieBreakTestData
{
    /// <summary>A distinct plan per supplier id (the id makes plans unequal).</summary>
    public static RoutingPlan Plan(string supplierId)
        => new(new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string> { [new Category("cane")] = supplierId }), 1, 8m, 1);

    public static RoutingPlan[] Plans(int count) => [.. Enumerable.Range(0, count).Select(i => Plan($"SUP-{i}"))];
}

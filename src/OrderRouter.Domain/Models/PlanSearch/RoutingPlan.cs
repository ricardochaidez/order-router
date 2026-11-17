using System.Collections.ObjectModel;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>One way to fulfill the order: each demanded category assigned to a supplier.</summary>
/// <param name="Assignments">Category to supplier id. Every chosen supplier receives at least one category.</param>
/// <param name="ShipmentCount">Number of distinct suppliers, which is the number of shipments.</param>
/// <param name="Quality">Mean effective rating, each order line counting once (FR-12).</param>
/// <param name="LocalLineCount">Order lines assigned to suppliers that deliver locally (ranking key 4, OR-013).</param>
public sealed record RoutingPlan(
    ReadOnlyDictionary<Category, string> Assignments,
    int ShipmentCount,
    decimal Quality,
    int LocalLineCount);

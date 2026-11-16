using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.UnitTests.Domain.Routing.PlanSearch;

internal static class PlanSearchTestData
{
    public static SupplierCandidate Local(string id, decimal rating, params string[] categories)
        => new(id, categories.Select(c => new Category(c)), FulfillmentMode.Local, rating);

    public static SupplierCandidate Mail(string id, decimal rating, params string[] categories)
        => new(id, categories.Select(c => new Category(c)), FulfillmentMode.Mail, rating);

    public static CategoryDemand Demand(string category, int lines = 1) => new(new Category(category), lines);

    public static string[] Ids(RoutingPlan plan) => [.. plan.Assignments.Values.Distinct().Order(StringComparer.Ordinal)];
}

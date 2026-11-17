using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Tests.TestData;

internal static class PlanSearchTestData
{
    public static SupplierCandidate Local(string id, decimal rating, params string[] categories)
        => new(id, categories.Select(c => new Category(c)), FulfillmentMode.Local, rating);

    public static SupplierCandidate Mail(string id, decimal rating, params string[] categories)
        => new(id, categories.Select(c => new Category(c)), FulfillmentMode.Mail, rating);

    public static CategoryDemand Demand(string category, int lines = 1) => new(new Category(category), lines);

    public static string[] Ids(RoutingPlan plan) => [.. plan.Assignments.Values.Distinct().Order(StringComparer.Ordinal)];
}

using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Models.PlanSearch;

public class PlanSearchRequestTests
{
    private static readonly Category cane = new("cane");

    private static SupplierCandidate Candidate(string id = "S1") => new(id, [cane], FulfillmentMode.Local, 8m);

    [Fact]
    public void Constructor_ValidInput_UsesDefaults()
    {
        var request = new PlanSearchRequest([new CategoryDemand(cane, 1)], [Candidate()]);

        Assert.Equal(0.5m, request.SimilarityBand);
        Assert.Equal(0, request.MaxExtraShipments);
    }

    [Fact]
    public void Constructor_NoCandidates_IsAllowed()
    {
        var request = new PlanSearchRequest([new CategoryDemand(cane, 1)], []);

        Assert.Empty(request.Candidates);
    }

    [Fact]
    public void Constructor_SeveralProblems_ReportsAllAtOnce()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new PlanSearchRequest([], [Candidate("A"), Candidate("A")], -1m, 2));

        Assert.Contains("at least one category", exception.Message);
        Assert.Contains("more than once", exception.Message);
        Assert.Contains("negative", exception.Message);
        Assert.Contains("0 or 1", exception.Message);
    }

    [Fact]
    public void Constructor_DuplicateCategory_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new PlanSearchRequest([new CategoryDemand(cane, 1), new CategoryDemand(new Category("CANE"), 2)], []));

        Assert.Contains("'cane' is demanded more than once", exception.Message);
    }

    [Fact]
    public void Constructor_MoreThan32Categories_Throws()
    {
        var demand = Enumerable.Range(0, 33).Select(i => new CategoryDemand(new Category($"c{i}"), 1));

        var exception = Assert.Throws<DomainValidationException>(() => new PlanSearchRequest(demand, []));

        Assert.Contains("at most 32", exception.Message);
    }

    [Fact]
    public void Constructor_Exactly32Categories_IsAllowed()
    {
        var demand = Enumerable.Range(0, 32).Select(i => new CategoryDemand(new Category($"c{i}"), 1));

        Assert.Equal(32, new PlanSearchRequest(demand, []).Demand.Count);
    }

    [Fact]
    public void Constructor_NullArguments_ReportsMissingDemand()
    {
        Assert.Throws<DomainValidationException>(() => new PlanSearchRequest(null, null));
    }

    [Fact]
    public void Constructor_InputListChangedLater_RequestIsUnaffected()
    {
        var demand = new List<CategoryDemand> { new(cane, 1) };

        var request = new PlanSearchRequest(demand, []);
        demand.Clear();

        Assert.Single(request.Demand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void CategoryDemand_LineCountBelowOne_Throws(int lineCount)
    {
        Assert.Throws<DomainValidationException>(() => new CategoryDemand(cane, lineCount));
    }

    [Fact]
    public void CategoryDemand_SameValues_AreEqual()
    {
        Assert.Equal(new CategoryDemand(cane, 2), new CategoryDemand(new Category(" Cane "), 2));
    }

    [Fact]
    public void SupplierCandidate_SeveralProblems_ReportsAllAtOnce()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new SupplierCandidate(" ", [], (FulfillmentMode)99, 11m));

        Assert.Contains("supplier id", exception.Message);
        Assert.Contains("eligible category", exception.Message);
        Assert.Contains("mode", exception.Message);
        Assert.Contains("rating", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void SupplierCandidate_RatingBoundaries_AreAccepted(double rating)
        => Assert.Equal((decimal)rating, new SupplierCandidate("S1", [cane], FulfillmentMode.Local, (decimal)rating).EffectiveRating);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10.01)]
    public void SupplierCandidate_RatingOutsideRange_Throws(double rating)
        => Assert.Throws<DomainValidationException>(() => new SupplierCandidate("S1", [cane], FulfillmentMode.Local, (decimal)rating));

    [Fact]
    public void SupplierCandidate_NullCategories_Throws()
    {
        Assert.Throws<DomainValidationException>(() => new SupplierCandidate("S1", null, FulfillmentMode.Mail, 5m));
    }

    [Fact]
    public void SupplierCandidate_CategoriesChangedLater_CandidateIsUnaffected()
    {
        var categories = new List<Category> { cane };

        var candidate = new SupplierCandidate(" S1 ", categories, FulfillmentMode.Local, 8m);
        categories.Clear();

        Assert.Equal("S1", candidate.SupplierId);
        Assert.Contains(cane, candidate.EligibleCategories);
    }
}

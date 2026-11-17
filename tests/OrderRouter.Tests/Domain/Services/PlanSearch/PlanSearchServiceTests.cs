using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Sections;
using OrderRouter.Domain.Services.PlanSearch;
using static OrderRouter.Tests.TestData.PlanSearchTestData;

namespace OrderRouter.Tests.Domain.Services.PlanSearch;

public class PlanSearchServiceTests
{
    private readonly ListLogger logger = new();

    private PlanSearchService Create(int maxCategories = 8, int budget = 200_000)
        => new(Options.Create(new PlanSearchOptions { MaxExactCategories = maxCategories, NodeBudget = budget }), logger);

    private static PlanSearchRequest Order(int categories)
    {
        var names = Enumerable.Range(0, categories).Select(i => $"c{i}").ToArray();

        return new PlanSearchRequest(names.Select(n => Demand(n)), [Local("S1", 8m, names)]);
    }

    [Fact]
    public void Search_TooManyCategories_LogsOneWarning()
    {
        var result = Create().Search(Order(9));

        Assert.Equal(SearchMode.Greedy, result.Mode);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("TooManyCategories", entry.Message);
        Assert.Contains("9 categories", entry.Message);
    }

    [Fact]
    public void Search_BudgetExceeded_LogsOneWarning()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("S1", 8m, "a"), Local("S2", 8m, "b"), Local("S3", 8m, "a", "b")]);

        var result = Create(budget: 1).Search(request);

        Assert.Equal(SearchFallbackReason.NodeBudgetExceeded, result.FallbackReason);
        Assert.Contains("NodeBudget", Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning).Message);
    }

    [Fact]
    public void Search_NormalOrder_StaysExactAndLogsNoWarning()
    {
        var result = Create().Search(Order(3));

        Assert.Equal(SearchMode.Exact, result.Mode);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void Search_UsesTheOptionsItWasGiven()
    {
        Assert.Equal(SearchMode.Greedy, Create(maxCategories: 2).Search(Order(3)).Mode);
        Assert.Equal(SearchMode.Exact, Create(maxCategories: 3).Search(Order(3)).Mode);
    }

    private sealed class ListLogger : ILogger<PlanSearchService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}

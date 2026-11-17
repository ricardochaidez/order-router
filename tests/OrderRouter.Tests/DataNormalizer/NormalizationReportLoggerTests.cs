using Microsoft.Extensions.Logging;
using OrderRouter.DataNormalizer.Reporting;

namespace OrderRouter.Tests.DataNormalizer;

public class NormalizationReportLoggerTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void Log_SkippedRowsAndDuplicates_WarnsPerItemAndSummarizesOnce()
    {
        var logger = new CapturingLogger();
        var report = new FileNormalizationReport(
            "products.csv", 10, 7, [new SkippedRow(4, "bad rating")], [new DuplicateRow("AB-1", [2, 5])]);

        NormalizationReportLogger.Log(logger, report);

        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        var summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information).Message;
        Assert.Contains("read 10", summary);
        Assert.Contains("written 7", summary);
        Assert.Contains("row 4: bad rating", summary);
        Assert.Contains("duplicates removed 1", summary);
    }

    [Fact]
    public void Log_CleanReport_WritesOnlyTheSummary()
    {
        var logger = new CapturingLogger();

        NormalizationReportLogger.Log(logger, new FileNormalizationReport("suppliers.csv", 3, 3, [], []));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("skipped 0 (none)", entry.Message);
    }
}

using Microsoft.Extensions.Logging;

namespace OrderRouter.DataNormalizer.Reporting;

/// <summary>
/// Logs the data-quality report (NFR-04, AC12) through Microsoft.Extensions.Logging: one warning per
/// skipped row and per duplicate, then one summary per file.
/// </summary>
public static class NormalizationReportLogger
{
    /// <summary>Logs one file's report.</summary>
    /// <param name="logger">Destination.</param>
    /// <param name="report">The report to log.</param>
    public static void Log(ILogger logger, FileNormalizationReport report)
    {
        foreach (var skipped in report.SkippedRows)
        {
            logger.LogWarning("{File} row {Row} skipped: {Reason}", report.FileName, skipped.RowNumber, skipped.Reason);
        }

        foreach (var duplicate in report.DuplicatesRemoved)
        {
            logger.LogWarning(
                "{File} duplicate removed: {Code} on rows {Rows} (first kept)",
                report.FileName,
                duplicate.Code,
                string.Join(", ", duplicate.RowNumbers));
        }

        var skippedDetail = report.SkippedRows.Count == 0
            ? "none"
            : string.Join("; ", report.SkippedRows.Select(skipped => $"row {skipped.RowNumber}: {skipped.Reason}"));

        logger.LogInformation(
            "{File}: read {Read}, written {Written}, skipped {Skipped} ({SkippedDetail}), duplicates removed {Duplicates}",
            report.FileName,
            report.RowsRead,
            report.RowsWritten,
            report.SkippedRows.Count,
            skippedDetail,
            report.DuplicatesRemoved.Count);
    }
}

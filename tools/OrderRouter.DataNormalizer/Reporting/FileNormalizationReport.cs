namespace OrderRouter.DataNormalizer.Reporting;

/// <summary>A source row that was left out of the output.</summary>
/// <param name="RowNumber">Physical line in the source file; the header is line 1, as seen in an editor.</param>
/// <param name="Reason">Why the row was skipped.</param>
public sealed record SkippedRow(int RowNumber, string Reason);

/// <summary>An identical duplicate that was collapsed into its first occurrence.</summary>
/// <param name="Code">The duplicated key (product code).</param>
/// <param name="RowNumbers">Source rows holding this key, the kept one first.</param>
public sealed record DuplicateRow(string Code, IReadOnlyList<int> RowNumbers);

/// <summary>
/// The data-quality summary for one file (NFR-04). Tests assert on this object; the console host logs it.
/// All collections are always present, never <c>null</c>.
/// </summary>
/// <param name="FileName">The source file name.</param>
/// <param name="RowsRead">Data rows read, excluding the header.</param>
/// <param name="RowsWritten">Rows that will be written to the normalized file.</param>
/// <param name="SkippedRows">Rows rejected, with row number and reason.</param>
/// <param name="DuplicatesRemoved">Identical duplicate rows that were collapsed.</param>
public sealed record FileNormalizationReport(
    string FileName,
    int RowsRead,
    int RowsWritten,
    IReadOnlyList<SkippedRow> SkippedRows,
    IReadOnlyList<DuplicateRow> DuplicatesRemoved);

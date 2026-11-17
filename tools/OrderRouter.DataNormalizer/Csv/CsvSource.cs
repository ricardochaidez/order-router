using System.Globalization;
using CsvHelper.Configuration;
using CsvHelper;
using OrderRouter.DataNormalizer.Reporting;

namespace OrderRouter.DataNormalizer.Csv;

/// <summary>A required column: a stable key plus every header spelling that is accepted for it.</summary>
/// <param name="Key">The name callers use to read the cell.</param>
/// <param name="HeaderNames">Accepted header spellings, case-insensitive (DQ-07: <c>suplier_name</c>).</param>
internal sealed record ColumnSpec(string Key, params string[] HeaderNames);

/// <summary>A data row: its physical line number and its cells by <see cref="ColumnSpec.Key"/>.</summary>
internal sealed record CsvRow(int RowNumber, IReadOnlyDictionary<string, string?> Cells);

/// <summary>
/// Reads a CSV through CsvHelper (quoted cells, CRLF and embedded commas) and maps columns by header
/// name. Extra columns are ignored (tolerant reader).
/// </summary>
internal static class CsvSource
{
    /// <summary>Reads every data row.</summary>
    /// <param name="reader">The CSV text.</param>
    /// <param name="fileName">Used in error messages.</param>
    /// <param name="columns">The required columns.</param>
    /// <exception cref="NormalizationException">The header is missing, or a column is missing or ambiguous.</exception>
    public static IReadOnlyList<CsvRow> Read(TextReader reader, string fileName, IReadOnlyList<ColumnSpec> columns)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Short rows give null cells instead of throwing, so the row can be skipped with a reason.
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, configuration);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } header)
        {
            throw new NormalizationException(fileName, "the file has no header row.");
        }

        var indexes = columns.ToDictionary(column => column.Key, column => ResolveColumn(header, column, fileName));
        var rows = new List<CsvRow>();

        while (csv.Read())
        {
            var cells = indexes.ToDictionary(pair => pair.Key, pair => csv.GetField(pair.Value));
            rows.Add(new CsvRow(csv.Parser.Row, cells));
        }

        return rows;
    }

    private static int ResolveColumn(string[] header, ColumnSpec column, string fileName)
    {
        var matches = Enumerable.Range(0, header.Length)
            .Where(index => column.HeaderNames.Contains(header[index].Trim(), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new NormalizationException(
                fileName, $"required column \"{column.HeaderNames[0]}\" is missing."),
            _ => throw new NormalizationException(
                fileName, $"column \"{column.Key}\" is ambiguous: more than one header matches ({string.Join(", ", column.HeaderNames)})."),
        };
    }
}

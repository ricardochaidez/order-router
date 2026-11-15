using OrderRouter.DataNormalizer.Csv;
using OrderRouter.DataNormalizer.Parsing;
using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.DataNormalizer.Suppliers;

/// <summary>The suppliers that survived normalization, plus the data-quality report.</summary>
/// <param name="Suppliers">Valid suppliers in source order.</param>
/// <param name="Report">What was read, written and skipped.</param>
public sealed record SupplierNormalizationResult(IReadOnlyList<Supplier> Suppliers, FileNormalizationReport Report);

/// <summary>
/// Turns the raw supplier CSV into domain <see cref="Supplier"/> objects. The normalizer only cleans
/// syntax (header aliases, ZIP padding, casing); building the <see cref="Supplier"/> is the validation,
/// so domain invariants are never weakened (OR-002).
/// </summary>
public static class SupplierNormalizer
{
    private const string idKey = "id";
    private const string nameKey = "name";
    private const string zipsKey = "zips";
    private const string categoriesKey = "categories";
    private const string ratingKey = "rating";
    private const string mailKey = "mail";

    // DQ-07: the source header is misspelled "suplier_name"; DQ-08: the flag column ends in "?".
    private static readonly ColumnSpec[] columns =
    [
        new(idKey, "supplier_id"),
        new(nameKey, "supplier_name", "suplier_name"),
        new(zipsKey, "service_zips"),
        new(categoriesKey, "product_categories"),
        new(ratingKey, "customer_satisfaction_score"),
        new(mailKey, "can_mail_order?"),
    ];

    /// <summary>Normalizes the supplier file.</summary>
    /// <param name="reader">The raw CSV text.</param>
    /// <param name="fileName">Source file name, used in the report and errors.</param>
    /// <returns>The valid suppliers and the report; invalid rows are skipped and reported (AC9).</returns>
    /// <exception cref="NormalizationException">A column is missing or ambiguous, or a supplier id repeats.</exception>
    public static SupplierNormalizationResult Normalize(TextReader reader, string fileName = "suppliers.csv")
    {
        var rows = CsvSource.Read(reader, fileName, columns);
        var suppliers = new List<Supplier>();
        var skipped = new List<SkippedRow>();
        var firstRowById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!TryBuildSupplier(row, out var supplier, out var reason))
            {
                skipped.Add(new SkippedRow(row.RowNumber, reason));
                continue;
            }

            // DQ-09/DQ-10: identity is the id. No safe way to pick between two rows, so fail the run.
            if (!firstRowById.TryAdd(supplier.Id, row.RowNumber))
            {
                throw new NormalizationException(
                    fileName,
                    $"duplicate supplier id \"{supplier.Id}\" on rows {firstRowById[supplier.Id]} and {row.RowNumber}.");
            }

            suppliers.Add(supplier);
        }

        var report = new FileNormalizationReport(fileName, rows.Count, suppliers.Count, skipped, []);

        return new SupplierNormalizationResult(suppliers, report);
    }

    private static bool TryBuildSupplier(CsvRow row, out Supplier supplier, out string reason)
    {
        supplier = null!;
        var errors = new List<string>();

        if (!ServiceZipsParser.TryParse(row.Cells[zipsKey], out var ranges, out var zipError))
        {
            errors.Add(zipError);
        }

        if (!RatingParser.TryParse(row.Cells[ratingKey], out var rating, out var ratingError))
        {
            errors.Add(ratingError);
        }

        if (!MailOrderFlagParser.TryParse(row.Cells[mailKey], out var canMailOrder, out var mailError))
        {
            errors.Add(mailError);
        }

        if (errors.Count > 0)
        {
            reason = string.Join("; ", errors);
            return false;
        }

        try
        {
            // Category lower-cases and de-duplicates (DQ-04); empty tokens are dropped, and an empty
            // result is rejected by Supplier. Rating range and rounding are enforced by Supplier too.
            var categories = (row.Cells[categoriesKey] ?? string.Empty)
                .Split(',')
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => new Category(token));

            supplier = new Supplier(
                row.Cells[idKey], row.Cells[nameKey], categories, new ServiceArea(ranges), rating, canMailOrder);
            reason = string.Empty;

            return true;
        }
        catch (DomainValidationException exception)
        {
            reason = exception.Message;

            return false;
        }
    }
}

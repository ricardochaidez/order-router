using OrderRouter.DataNormalizer.Csv;
using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models;

namespace OrderRouter.DataNormalizer.Products;

/// <summary>The products that survived normalization, plus the data-quality report.</summary>
/// <param name="Products">Valid, de-duplicated products in source order.</param>
/// <param name="Report">What was read, written, skipped and collapsed.</param>
public sealed record ProductNormalizationResult(IReadOnlyList<Product> Products, FileNormalizationReport Report);

/// <summary>
/// Turns the raw product CSV into domain <see cref="Product"/> objects and resolves duplicate codes.
/// </summary>
public static class ProductNormalizer
{
    private const string codeKey = "code";
    private const string nameKey = "name";
    private const string categoryKey = "category";

    private static readonly ColumnSpec[] columns =
    [
        new(codeKey, "product_code"),
        new(nameKey, "product_name"),
        new(categoryKey, "category"),
    ];

    /// <summary>Normalizes the product file.</summary>
    /// <param name="reader">The raw CSV text.</param>
    /// <param name="fileName">Source file name, used in the report and errors.</param>
    /// <returns>
    /// The valid products and the report. Invalid rows are skipped and reported. Identical duplicates
    /// keep the first occurrence and are reported (DQ-11, AC7).
    /// </returns>
    /// <exception cref="NormalizationException">
    /// A column is missing, or two rows share a code but differ in name or category (DQ-12, AC8).
    /// </exception>
    public static ProductNormalizationResult Normalize(TextReader reader, string fileName = "products.csv")
    {
        var rows = CsvSource.Read(reader, fileName, columns);
        var products = new List<Product>();
        var skipped = new List<SkippedRow>();
        var kept = new Dictionary<string, (Product Product, List<int> RowNumbers)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!TryBuildProduct(row, out var product, out var reason))
            {
                skipped.Add(new SkippedRow(row.RowNumber, reason));
                continue;
            }

            // FR-05 looks products up case-insensitively, so codes that differ only by case collide.
            if (!kept.TryGetValue(product.Code, out var existing))
            {
                kept[product.Code] = (product, [row.RowNumber]);
                products.Add(product);
                continue;
            }

            // Fail safe (NFR-10): if any normalized field differs there is no safe row to keep.
            if (existing.Product.Category != product.Category
                || !string.Equals(existing.Product.Name, product.Name, StringComparison.Ordinal))
            {
                throw new NormalizationException(
                    fileName,
                    $"conflicting duplicate product code \"{product.Code}\" on rows {existing.RowNumbers[0]} and {row.RowNumber}: "
                    + $"name/category differ (\"{existing.Product.Name}\"/{existing.Product.Category} vs \"{product.Name}\"/{product.Category}).");
            }

            existing.RowNumbers.Add(row.RowNumber);
        }

        var duplicates = kept.Values
            .Where(entry => entry.RowNumbers.Count > 1)
            .Select(entry => new DuplicateRow(entry.Product.Code, entry.RowNumbers))
            .ToList();
        var report = new FileNormalizationReport(fileName, rows.Count, products.Count, skipped, duplicates);

        return new ProductNormalizationResult(products, report);
    }

    private static bool TryBuildProduct(CsvRow row, out Product product, out string reason)
    {
        product = null!;

        if (!Category.TryCreate(row.Cells[categoryKey], out var category))
        {
            // Still build the product so a bad code or name is reported together with the category.
            reason = "category must not be empty" + ExtraReason(row);
            return false;
        }

        try
        {
            product = new Product(row.Cells[codeKey], row.Cells[nameKey], category);
            reason = string.Empty;

            return true;
        }
        catch (DomainValidationException exception)
        {
            reason = exception.Message;

            return false;
        }
    }

    private static string ExtraReason(CsvRow row)
        => string.IsNullOrWhiteSpace(row.Cells[codeKey]) || string.IsNullOrWhiteSpace(row.Cells[nameKey])
            ? "; code and name must not be empty"
            : string.Empty;
}

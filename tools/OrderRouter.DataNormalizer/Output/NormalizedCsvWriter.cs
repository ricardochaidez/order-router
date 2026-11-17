using System.Globalization;
using CsvHelper.Configuration;
using CsvHelper;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.DataNormalizer.Output;

/// <summary>
/// Writes the normalized seed files in a deterministic format (AC13): header always written, rows in
/// source order, LF line endings, invariant culture. OR-005 reads exactly this format.
/// </summary>
public static class NormalizedCsvWriter
{
    /// <summary>Header of <c>suppliers.normalized.csv</c>.</summary>
    public static readonly IReadOnlyList<string> SupplierHeader =
        ["supplier_id", "supplier_name", "service_zips", "product_categories", "rating", "can_mail_order"];

    /// <summary>Header of <c>products.normalized.csv</c>.</summary>
    public static readonly IReadOnlyList<string> ProductHeader = ["product_code", "product_name", "category"];

    /// <summary>Writes suppliers: padded ZIPs, sorted lower-case categories, <c>0.##</c> rating, <c>y</c>/<c>n</c>.</summary>
    /// <param name="writer">Destination; the caller chooses the encoding (UTF-8 without BOM).</param>
    /// <param name="suppliers">Suppliers in the order to write.</param>
    /// <example>
    /// <code>
    /// SUP-001,Acme,"02130, 02114-02212","cane, cpap",7,y
    /// </code>
    /// </example>
    public static void WriteSuppliers(TextWriter writer, IEnumerable<Supplier> suppliers)
    {
        using var csv = CreateWriter(writer);
        WriteRecord(csv, SupplierHeader);

        foreach (var supplier in suppliers)
        {
            WriteRecord(
                csv,
                [
                    supplier.Id,
                    supplier.Name,
                    string.Join(", ", supplier.ServiceArea.Segments.Select(FormatRange)),
                    // Ordinal sort: the set has no order of its own, and the output must be byte-stable.
                    string.Join(", ", supplier.Categories.Select(category => category.Value).Order(StringComparer.Ordinal)),
                    // Empty means unrated (never 0); "0.##" turns 10.0 into 10.
                    supplier.Rating?.Value.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    supplier.CanMailOrder ? "y" : "n",
                ]);
        }
    }

    /// <summary>Writes products: trimmed values and a lower-case category.</summary>
    /// <param name="writer">Destination; the caller chooses the encoding (UTF-8 without BOM).</param>
    /// <param name="products">Products in the order to write.</param>
    public static void WriteProducts(TextWriter writer, IEnumerable<Product> products)
    {
        using var csv = CreateWriter(writer);
        WriteRecord(csv, ProductHeader);

        foreach (var product in products)
        {
            WriteRecord(csv, [product.Code, product.Name, product.Category.Value]);
        }
    }

    // A single ZIP is NNNNN and a range is NNNNN-NNNNN; both parse with ZipRange.Parse.
    private static string FormatRange(ZipRange range)
        => range.Start == range.End ? range.Start.Value : range.ToString();

    // CsvHelper quotes only when needed (commas, quotes, line breaks), which keeps output deterministic.
    private static CsvWriter CreateWriter(TextWriter writer)
        => new(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" }, leaveOpen: true);

    private static void WriteRecord(CsvWriter csv, IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            csv.WriteField(field);
        }

        csv.NextRecord();
    }
}

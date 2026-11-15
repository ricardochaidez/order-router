using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Infrastructure.Persistence.Seeding;

/// <summary>
/// Reads the normalized seed files written by OR-003 into domain objects. Reading <b>is</b> the
/// validation: every row goes through the OR-002 constructors, and nothing is fixed, padded,
/// trimmed or de-duplicated here (NFR-10). The first rejected row stops the read.
/// </summary>
public static class NormalizedSeedFileReader
{
    /// <summary>Required columns of <c>suppliers.normalized.csv</c>. A test keeps this equal to the OR-003 writer's header.</summary>
    public static readonly IReadOnlyList<string> SupplierHeader =
        ["supplier_id", "supplier_name", "service_zips", "product_categories", "rating", "can_mail_order"];

    /// <summary>Required columns of <c>products.normalized.csv</c>. A test keeps this equal to the OR-003 writer's header.</summary>
    public static readonly IReadOnlyList<string> ProductHeader = ["product_code", "product_name", "category"];

    private const string listSeparator = ", ";

    /// <summary>Reads and validates the suppliers file.</summary>
    /// <param name="path">Path to <c>suppliers.normalized.csv</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>All suppliers, in file order.</returns>
    /// <exception cref="ReferenceDataSeedException">The file is missing or malformed, or a row is rejected.</exception>
    public static async Task<IReadOnlyList<Supplier>> ReadSuppliersAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = OpenFile(path);

        return await ReadSuppliersAsync(reader, Path.GetFileName(path), cancellationToken);
    }

    /// <summary>Reads and validates suppliers from any text source (used by unit tests).</summary>
    /// <param name="reader">Source of CSV text.</param>
    /// <param name="fileName">Name used in error messages.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>All suppliers, in file order.</returns>
    /// <exception cref="ReferenceDataSeedException">The text is malformed, or a row is rejected.</exception>
    public static async Task<IReadOnlyList<Supplier>> ReadSuppliersAsync(
        TextReader reader, string fileName, CancellationToken cancellationToken)
    {
        var suppliers = new List<Supplier>();
        // supplier_id is the primary key and is compared ordinally, like the database does.
        var rowsById = new Dictionary<string, int>(StringComparer.Ordinal);

        await ReadRowsAsync(
            reader,
            fileName,
            SupplierHeader,
            cancellationToken,
            (csv, row) =>
            {
                var supplier = ParseSupplier(csv, fileName, row);

                if (rowsById.TryGetValue(supplier.Id, out var firstRow))
                {
                    throw new ReferenceDataSeedException(
                        fileName, row, $"duplicate supplier_id \"{supplier.Id}\" (first seen on row {firstRow})");
                }

                // Two segments with one start ZIP would collide on the PK (supplier_id, start_zip).
                var repeated = supplier.ServiceArea.Segments
                    .GroupBy(segment => segment.Start)
                    .FirstOrDefault(group => group.Count() > 1);

                if (repeated is not null)
                {
                    throw new ReferenceDataSeedException(
                        fileName, row, $"supplier \"{supplier.Id}\" has more than one service area segment starting at ZIP {repeated.Key}");
                }

                rowsById.Add(supplier.Id, row);
                suppliers.Add(supplier);
            });

        return suppliers;
    }

    /// <summary>Reads and validates the products file.</summary>
    /// <param name="path">Path to <c>products.normalized.csv</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>All products, in file order.</returns>
    /// <exception cref="ReferenceDataSeedException">The file is missing or malformed, or a row is rejected.</exception>
    public static async Task<IReadOnlyList<Product>> ReadProductsAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = OpenFile(path);

        return await ReadProductsAsync(reader, Path.GetFileName(path), cancellationToken);
    }

    /// <summary>Reads and validates products from any text source (used by unit tests).</summary>
    /// <param name="reader">Source of CSV text.</param>
    /// <param name="fileName">Name used in error messages.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>All products, in file order.</returns>
    /// <exception cref="ReferenceDataSeedException">The text is malformed, or a row is rejected.</exception>
    public static async Task<IReadOnlyList<Product>> ReadProductsAsync(
        TextReader reader, string fileName, CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        // Lookups ignore case (FR-05), so codes that differ only in case would be ambiguous: fail safe.
        var rowsByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await ReadRowsAsync(
            reader,
            fileName,
            ProductHeader,
            cancellationToken,
            (csv, row) =>
            {
                var product = ParseProduct(csv, fileName, row);

                if (rowsByCode.TryGetValue(product.Code, out var firstRow))
                {
                    throw new ReferenceDataSeedException(
                        fileName, row, $"duplicate product_code \"{product.Code}\" ignoring case (first seen on row {firstRow})");
                }

                rowsByCode.Add(product.Code, row);
                products.Add(product);
            });

        return products;
    }

    // A missing file is a fail-fast condition (AC5); the message names the full path.
    private static StreamReader OpenFile(string path)
    {
        var fullPath = Path.GetFullPath(path);

        return File.Exists(fullPath)
            ? new StreamReader(fullPath, Encoding.UTF8)
            : throw new ReferenceDataSeedException(fullPath, null, "seed file not found");
    }

    private static async Task ReadRowsAsync(
        TextReader reader,
        string fileName,
        IReadOnlyList<string> requiredColumns,
        CancellationToken cancellationToken,
        Action<CsvReader, int> readRow)
    {
        // CsvHelper parses the CSV (quoting, commas inside fields). Culture is invariant and nothing is
        // trimmed, so the reader sees exactly what OR-003 wrote.
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.None,
        };

        using var csv = new CsvReader(reader, configuration);
        var rowCount = 0;
        var row = 0;

        try
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } header)
            {
                throw new ReferenceDataSeedException(fileName, null, "file has no header row");
            }

            var missing = requiredColumns.Where(column => !header.Contains(column, StringComparer.Ordinal)).ToArray();

            if (missing.Length > 0)
            {
                throw new ReferenceDataSeedException(
                    fileName, null, $"header is missing required column(s): {string.Join(", ", missing)}");
            }

            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Parser.Row counts records and the header is row 1, the same numbering OR-003 reports.
                row = csv.Parser.Row;
                readRow(csv, row);
                rowCount++;
            }
        }
        catch (CsvHelperException exception)
        {
            throw new ReferenceDataSeedException(fileName, row > 0 ? row : null, exception.Message.Split('\n')[0].Trim(), exception);
        }

        if (rowCount == 0)
        {
            throw new ReferenceDataSeedException(fileName, null, "file has no data rows");
        }
    }

    private static Supplier ParseSupplier(CsvReader csv, string fileName, int row)
    {
        try
        {
            var zipTokens = (csv.GetField("service_zips") ?? string.Empty).Split(listSeparator);
            var categoryTokens = (csv.GetField("product_categories") ?? string.Empty).Split(listSeparator);

            var serviceArea = new ServiceArea(zipTokens.Select(ZipRange.Parse));
            var categories = categoryTokens.Select(token => new Category(token)).ToList();

            return new Supplier(
                csv.GetField("supplier_id"),
                csv.GetField("supplier_name"),
                categories,
                serviceArea,
                ParseRating(csv.GetField("rating")),
                ParseMailOrder(csv.GetField("can_mail_order")));
        }
        catch (DomainValidationException exception)
        {
            throw new ReferenceDataSeedException(fileName, row, exception.Message, exception);
        }
    }

    private static Product ParseProduct(CsvReader csv, string fileName, int row)
    {
        try
        {
            return new Product(
                csv.GetField("product_code"),
                csv.GetField("product_name"),
                new Category(csv.GetField("category")));
        }
        catch (DomainValidationException exception)
        {
            throw new ReferenceDataSeedException(fileName, row, exception.Message, exception);
        }
    }

    // Empty means unrated (never 0, DQ-06). The 1-10 range check belongs to the Supplier constructor.
    private static decimal? ParseRating(string? raw)
        => string.IsNullOrEmpty(raw)
            ? null
            : decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rating)
                ? rating
                : throw new DomainValidationException($"rating must be a number or empty (received \"{raw}\")");

    // OR-003 writes exactly y or n; anything else is a data problem, not something to guess at.
    private static bool ParseMailOrder(string? raw)
        => raw switch
        {
            "y" => true,
            "n" => false,
            _ => throw new DomainValidationException($"can_mail_order must be exactly \"y\" or \"n\" (received {(raw is null ? "null" : $"\"{raw}\"")})"),
        };
}

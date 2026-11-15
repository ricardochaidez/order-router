using System.Text;
using OrderRouter.DataNormalizer.Output;
using OrderRouter.DataNormalizer.Products;
using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.DataNormalizer.Suppliers;

namespace OrderRouter.DataNormalizer;

/// <summary>Both file reports of a successful run.</summary>
/// <param name="Suppliers">Report for the supplier file.</param>
/// <param name="Products">Report for the product file.</param>
public sealed record NormalizationRunResult(FileNormalizationReport Suppliers, FileNormalizationReport Products);

/// <summary>
/// Reads the raw files, normalizes both, then writes both outputs or neither. All file I/O lives here
/// so the normalizers and writer stay pure.
/// </summary>
public static class NormalizationRunner
{
    /// <summary>Raw supplier file name.</summary>
    public const string SuppliersInputFile = "suppliers.csv";

    /// <summary>Raw product file name.</summary>
    public const string ProductsInputFile = "products.csv";

    /// <summary>Normalized supplier file name.</summary>
    public const string SuppliersOutputFile = "suppliers.normalized.csv";

    /// <summary>Normalized product file name.</summary>
    public const string ProductsOutputFile = "products.normalized.csv";

    // UTF-8 without a BOM keeps the committed files byte-stable and diff-friendly (AC13).
    private static readonly UTF8Encoding utf8NoBom = new(false);

    /// <summary>Normalizes <c>suppliers.csv</c> and <c>products.csv</c> from <paramref name="inputDirectory"/>.</summary>
    /// <param name="inputDirectory">Folder holding the raw files; opened read-only, never modified (AC11).</param>
    /// <param name="outputDirectory">Folder for the normalized files; created if missing.</param>
    /// <returns>The report for each file.</returns>
    /// <exception cref="NormalizationException">A fatal file-level problem; nothing was written.</exception>
    public static NormalizationRunResult Run(string inputDirectory, string outputDirectory)
    {
        var suppliers = SupplierNormalizer.Normalize(OpenRead(Path.Combine(inputDirectory, SuppliersInputFile)), SuppliersInputFile);
        var products = ProductNormalizer.Normalize(OpenRead(Path.Combine(inputDirectory, ProductsInputFile)), ProductsInputFile);

        // Render both in memory first: a failure above, or while formatting, leaves the output folder untouched.
        var supplierBytes = Render(writer => NormalizedCsvWriter.WriteSuppliers(writer, suppliers.Suppliers));
        var productBytes = Render(writer => NormalizedCsvWriter.WriteProducts(writer, products.Products));

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, SuppliersOutputFile), supplierBytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, ProductsOutputFile), productBytes);

        return new NormalizationRunResult(suppliers.Report, products.Report);
    }

    // Read + FileShare.Read only: the source files must never be changed (AC11).
    private static StreamReader OpenRead(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return new StreamReader(stream, utf8NoBom, detectEncodingFromByteOrderMarks: true);
    }

    private static byte[] Render(Action<TextWriter> write)
    {
        using var buffer = new MemoryStream();

        using (var writer = new StreamWriter(buffer, utf8NoBom, leaveOpen: true))
        {
            write(writer);
        }

        return buffer.ToArray();
    }
}

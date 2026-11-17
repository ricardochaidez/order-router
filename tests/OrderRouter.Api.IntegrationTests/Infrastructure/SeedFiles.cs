using OrderRouter.Infrastructure.Services.Seeding;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Writes small normalized CSVs to a temp folder for the throwaway-database tests, and deletes the folder
/// on dispose. The real <c>Docs/Data/Seed</c> files are never edited.
/// </summary>
public sealed class SeedFiles : IDisposable
{
    public const string SupplierHeader = "supplier_id,supplier_name,service_zips,product_categories,rating,can_mail_order";
    public const string ProductHeader = "product_code,product_name,category";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "order-router-tests", Guid.NewGuid().ToString("N"));

    private SeedFiles(string[] supplierLines, string[] productLines)
    {
        Directory.CreateDirectory(directory);
        Files = ReferenceDataSeedFiles.FromDirectory(directory);
        File.WriteAllLines(Files.SuppliersPath, supplierLines);
        File.WriteAllLines(Files.ProductsPath, productLines);
    }

    /// <summary>Paths of the written files.</summary>
    public ReferenceDataSeedFiles Files { get; }

    /// <summary>The committed seed folder, found by walking up to <c>OrderRouter.slnx</c>.</summary>
    public static string RealSeedDirectory { get; } = FindRealSeedDirectory();

    /// <summary>Paths of the committed seed files.</summary>
    public static ReferenceDataSeedFiles Real => ReferenceDataSeedFiles.FromDirectory(RealSeedDirectory);

    /// <summary>Writes the given data rows under the standard headers.</summary>
    public static SeedFiles Create(string[] supplierRows, string[] productRows)
        => new([SupplierHeader, .. supplierRows], [ProductHeader, .. productRows]);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static string FindRealSeedDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null && !File.Exists(Path.Combine(current.FullName, "OrderRouter.slnx")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Could not find OrderRouter.slnx above the test output directory.")
            : Path.Combine(current.FullName, "Docs", "Data", "Seed");
    }
}

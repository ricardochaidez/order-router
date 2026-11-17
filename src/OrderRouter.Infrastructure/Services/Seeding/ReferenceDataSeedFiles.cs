namespace OrderRouter.Infrastructure.Services.Seeding;

/// <summary>Paths of the two normalized seed files produced by the OR-003 normalizer.</summary>
/// <param name="SuppliersPath">Path to <c>suppliers.normalized.csv</c>.</param>
/// <param name="ProductsPath">Path to <c>products.normalized.csv</c>.</param>
public sealed record ReferenceDataSeedFiles(string SuppliersPath, string ProductsPath)
{
    /// <summary>File name of the normalized suppliers file.</summary>
    public const string SuppliersFileName = "suppliers.normalized.csv";

    /// <summary>File name of the normalized products file.</summary>
    public const string ProductsFileName = "products.normalized.csv";

    /// <summary>Builds the paths for a folder that holds both normalized files.</summary>
    /// <param name="seedDirectory">Folder such as <c>Docs/Data/Seed</c>.</param>
    /// <example>
    /// <code>
    /// var files = ReferenceDataSeedFiles.FromDirectory("Docs/Data/Seed");
    /// </code>
    /// </example>
    public static ReferenceDataSeedFiles FromDirectory(string seedDirectory)
        => new(Path.Combine(seedDirectory, SuppliersFileName), Path.Combine(seedDirectory, ProductsFileName));
}

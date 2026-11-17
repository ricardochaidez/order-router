using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper.Configuration;
using CsvHelper;
using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.DataNormalizer.Suppliers;
using OrderRouter.DataNormalizer;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Tests.DataNormalizer;

public class NormalizationRunnerTests
{
    private static readonly string[] sourceFiles = ["suppliers.csv", "products.csv", "sample_orders.json"];

    private static List<Dictionary<string, string>> ReadRows(string path)
    {
        using var csv = new CsvReader(new StreamReader(path), new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.Read();
        csv.ReadHeader();
        var rows = new List<Dictionary<string, string>>();

        while (csv.Read())
        {
            rows.Add(csv.HeaderRecord!.ToDictionary(name => name, name => csv.GetField(name)!));
        }

        return rows;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void Run_RealData_WritesBothFilesWithExpectedCounts()
    {
        var output = TestRepository.NewTempDirectory();

        var result = NormalizationRunner.Run(TestRepository.DataDirectory, output);

        Assert.True(File.Exists(Path.Combine(output, NormalizationRunner.SuppliersOutputFile)));
        Assert.True(File.Exists(Path.Combine(output, NormalizationRunner.ProductsOutputFile)));
        Assert.Equal((1100, 1100, 0), (result.Suppliers.RowsRead, result.Suppliers.RowsWritten, result.Suppliers.SkippedRows.Count));
        Assert.Equal((1200, 1195, 0, 5), (result.Products.RowsRead, result.Products.RowsWritten, result.Products.SkippedRows.Count, result.Products.DuplicatesRemoved.Count));
    }

    [Fact]
    public void Run_RealData_LeavesSourceFilesUnchanged()
    {
        var before = sourceFiles.Select(file => Hash(Path.Combine(TestRepository.DataDirectory, file))).ToArray();

        NormalizationRunner.Run(TestRepository.DataDirectory, TestRepository.NewTempDirectory());

        var after = sourceFiles.Select(file => Hash(Path.Combine(TestRepository.DataDirectory, file))).ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Run_TwiceOnSameInput_ProducesByteIdenticalFiles()
    {
        var first = TestRepository.NewTempDirectory();
        var second = TestRepository.NewTempDirectory();

        NormalizationRunner.Run(TestRepository.DataDirectory, first);
        NormalizationRunner.Run(TestRepository.DataDirectory, second);

        foreach (var file in new[] { NormalizationRunner.SuppliersOutputFile, NormalizationRunner.ProductsOutputFile })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, file)), File.ReadAllBytes(Path.Combine(second, file)));
        }
    }

    [Fact]
    public void Run_RealData_OutputHasNoBomAndNoCarriageReturns()
    {
        var output = TestRepository.NewTempDirectory();

        NormalizationRunner.Run(TestRepository.DataDirectory, output);

        foreach (var file in new[] { NormalizationRunner.SuppliersOutputFile, NormalizationRunner.ProductsOutputFile })
        {
            var bytes = File.ReadAllBytes(Path.Combine(output, file));
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.DoesNotContain((byte)'\r', bytes);
        }
    }

    [Fact]
    public void Run_RealData_WrittenSuppliersRebuildTheSameDomainObjects()
    {
        var output = TestRepository.NewTempDirectory();
        NormalizationRunner.Run(TestRepository.DataDirectory, output);
        using var source = File.OpenText(Path.Combine(TestRepository.DataDirectory, "suppliers.csv"));
        var expected = SupplierNormalizer.Normalize(source).Suppliers;

        var rows = ReadRows(Path.Combine(output, NormalizationRunner.SuppliersOutputFile));

        Assert.Equal(expected.Count, rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rebuilt = new Supplier(
                row["supplier_id"],
                row["supplier_name"],
                row["product_categories"].Split(", ").Select(category => new Category(category)),
                new ServiceArea(row["service_zips"].Split(", ").Select(ZipRange.Parse)),
                row["rating"].Length == 0 ? null : decimal.Parse(row["rating"], CultureInfo.InvariantCulture),
                row["can_mail_order"] == "y");
            var original = expected[index];

            Assert.Equal(original.Id, rebuilt.Id);
            Assert.Equal(original.Name, rebuilt.Name);
            Assert.Equal(original.Rating, rebuilt.Rating);
            Assert.Equal(original.CanMailOrder, rebuilt.CanMailOrder);
            Assert.True(original.Categories.SetEquals(rebuilt.Categories));
            Assert.Equal(original.ServiceArea.Segments, rebuilt.ServiceArea.Segments);
        }
    }

    [Fact]
    public void Run_RealData_WrittenProductsRebuildTheSameDomainObjects()
    {
        var output = TestRepository.NewTempDirectory();

        NormalizationRunner.Run(TestRepository.DataDirectory, output);

        var rows = ReadRows(Path.Combine(output, NormalizationRunner.ProductsOutputFile));
        Assert.Equal(1195, rows.Count);
        Assert.All(rows, row => Assert.Equal(row["category"], new Product(row["product_code"], row["product_name"], new Category(row["category"])).Category.Value));
    }

    [Fact]
    public void Run_ConflictingDuplicateProduct_WritesNothingAndNamesTheCode()
    {
        var input = TestRepository.NewTempDirectory();
        var output = Path.Combine(TestRepository.NewTempDirectory(), "Seed");
        File.Copy(Path.Combine(TestRepository.DataDirectory, "suppliers.csv"), Path.Combine(input, "suppliers.csv"));
        File.WriteAllText(Path.Combine(input, "products.csv"), "product_code,product_name,category\nAB-1,Name,cane\nAB-1,Name,crutches\n");

        var exception = Assert.Throws<NormalizationException>(() => NormalizationRunner.Run(input, output));

        Assert.Contains("AB-1", exception.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Run_FailedRun_LeavesExistingOutputUntouched()
    {
        var input = TestRepository.NewTempDirectory();
        var output = TestRepository.NewTempDirectory();
        var existing = Path.Combine(output, NormalizationRunner.SuppliersOutputFile);
        File.WriteAllText(existing, "previous");
        File.Copy(Path.Combine(TestRepository.DataDirectory, "suppliers.csv"), Path.Combine(input, "suppliers.csv"));
        File.WriteAllText(Path.Combine(input, "products.csv"), "product_code,product_name\n");

        Assert.Throws<NormalizationException>(() => NormalizationRunner.Run(input, output));

        Assert.Equal("previous", File.ReadAllText(existing, Encoding.UTF8));
    }

    [Fact]
    public void Run_MissingInputFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => NormalizationRunner.Run(TestRepository.NewTempDirectory(), TestRepository.NewTempDirectory()));
    }

    [Fact]
    public void CommittedSeedFiles_EqualAFreshGeneration()
    {
        var output = TestRepository.NewTempDirectory();

        NormalizationRunner.Run(TestRepository.DataDirectory, output);

        // Fails when a source CSV changed but Docs/Data/Seed was not regenerated (ADR-0002 stale-seed risk).
        // Normalize line endings so a CRLF checkout on Windows cannot cause a false failure.
        foreach (var file in new[] { NormalizationRunner.SuppliersOutputFile, NormalizationRunner.ProductsOutputFile })
        {
            var committed = File.ReadAllText(Path.Combine(TestRepository.SeedDirectory, file)).ReplaceLineEndings("\n");
            var fresh = File.ReadAllText(Path.Combine(output, file));
            Assert.Equal(fresh, committed);
        }
    }
}

using OrderRouter.DataNormalizer.Output;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Infrastructure.Services.Seeding;
using OrderRouter.Tests.DataNormalizer;

namespace OrderRouter.Tests.Infrastructure.Services.Seeding;

public class NormalizedSeedFileReaderTests
{
    private const string supplierHeaderLine = "supplier_id,supplier_name,service_zips,product_categories,rating,can_mail_order";
    private const string productHeaderLine = "product_code,product_name,category";
    private const string goodSupplierRow = "S1,Acme,\"02130, 02114-02212\",\"cane, cpap\",7.5,y";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<IReadOnlyList<Supplier>> ReadSuppliers(params string[] lines)
        => NormalizedSeedFileReader.ReadSuppliersAsync(new StringReader(string.Join('\n', lines)), "suppliers.csv", Token);

    private static Task<IReadOnlyList<Product>> ReadProducts(params string[] lines)
        => NormalizedSeedFileReader.ReadProductsAsync(new StringReader(string.Join('\n', lines)), "products.csv", Token);

    private static async Task<ReferenceDataSeedException> RejectedSupplier(params string[] lines)
        => await Assert.ThrowsAsync<ReferenceDataSeedException>(() => ReadSuppliers(lines));

    [Fact]
    public async Task ReadSuppliersAsync_RealSeedFile_ReadsEveryRowIncludingSentinels()
    {
        var path = Path.Combine(TestRepository.SeedDirectory, ReferenceDataSeedFiles.SuppliersFileName);

        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(path, Token);

        var dataRows = (await File.ReadAllLinesAsync(path, Token)).Length - 1;
        Assert.Equal(dataRows, suppliers.Count);
        for (var number = 1; number <= 14; number++)
        {
            Assert.Contains(suppliers, supplier => supplier.Id == $"SUP-T{number:D3}");
        }
    }

    [Fact]
    public async Task ReadProductsAsync_RealSeedFile_ReadsEveryRow()
    {
        var path = Path.Combine(TestRepository.SeedDirectory, ReferenceDataSeedFiles.ProductsFileName);

        var products = await NormalizedSeedFileReader.ReadProductsAsync(path, Token);

        Assert.Equal((await File.ReadAllLinesAsync(path, Token)).Length - 1, products.Count);
    }

    [Fact]
    public void Headers_AreEqualToTheNormalizerWriterHeaders()
    {
        Assert.Equal(NormalizedCsvWriter.SupplierHeader, NormalizedSeedFileReader.SupplierHeader);
        Assert.Equal(NormalizedCsvWriter.ProductHeader, NormalizedSeedFileReader.ProductHeader);
    }

    [Fact]
    public async Task ReadSuppliersAsync_RowWrittenByNormalizer_RoundTripsToEqualValues()
    {
        var original = new Supplier(
            "S1",
            "Acme, Inc.",
            [new Category("cpap"), new Category("cane")],
            new ServiceArea([ZipRange.Parse("02130"), ZipRange.Parse("02114-02212")]),
            6.75m,
            true);
        var unrated = new Supplier("S2", "Beta", [new Category("oxygen")], new ServiceArea([ZipRange.Parse("00100-99999")]), null, false);
        using var writer = new StringWriter();
        NormalizedCsvWriter.WriteSuppliers(writer, [original, unrated]);

        var read = await NormalizedSeedFileReader.ReadSuppliersAsync(new StringReader(writer.ToString()), "s.csv", Token);

        Assert.Equal(2, read.Count);
        Assert.Equal(original.Name, read[0].Name);
        Assert.Equal(original.Categories.ToHashSet(), read[0].Categories.ToHashSet());
        Assert.Equal(original.ServiceArea.Segments, read[0].ServiceArea.Segments);
        Assert.Equal(6.75m, read[0].Rating!.Value.Value);
        Assert.True(read[0].CanMailOrder);
        Assert.Null(read[1].Rating);
        Assert.False(read[1].CanMailOrder);
    }

    [Fact]
    public async Task ReadProductsAsync_RowWrittenByNormalizer_RoundTrips()
    {
        var original = new Product("AB-1", "Cane, quad", new Category("cane"));
        using var writer = new StringWriter();
        NormalizedCsvWriter.WriteProducts(writer, [original]);

        var read = await NormalizedSeedFileReader.ReadProductsAsync(new StringReader(writer.ToString()), "p.csv", Token);

        var product = Assert.Single(read);
        Assert.Equal(("AB-1", "Cane, quad", "cane"), (product.Code, product.Name, product.Category.Value));
    }

    [Theory]
    [InlineData("S1,Acme,2130,cane,7,y", "ZIP")]
    [InlineData("S1,Acme,02130 ,cane,7,y", "ZIP")]
    [InlineData("S1,Acme,02130,cane,11,y", "Rating")]
    [InlineData("S1,Acme,02130,cane,abc,y", "rating must be a number")]
    [InlineData("S1,Acme,02130,\"cane, \",7,y", "Category")]
    [InlineData("S1,Acme,02130,cane,7,Y", "can_mail_order")]
    [InlineData("S1,Acme,02130,cane,7,yes", "can_mail_order")]
    [InlineData("S1,,02130,cane,7,y", "name must not be empty")]
    [InlineData(",Acme,02130,cane,7,y", "id must not be empty")]
    public async Task ReadSuppliersAsync_InvalidRow_ThrowsWithFileRowAndReason(string row, string expectedReason)
    {
        var exception = await RejectedSupplier(supplierHeaderLine, goodSupplierRow.Replace("S1", "S0", StringComparison.Ordinal), row);

        Assert.Equal("suppliers.csv", exception.FileName);
        Assert.Equal(3, exception.RowNumber);
        Assert.Contains(expectedReason, exception.Reason, StringComparison.Ordinal);
        Assert.StartsWith("suppliers.csv row 3: ", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task ReadSuppliersAsync_SeveralBadRows_ReportsOnlyTheFirst()
    {
        var exception = await RejectedSupplier(
            supplierHeaderLine, "S1,Acme,2130,cane,7,y", "S2,Beta,02130,cane,11,y");

        Assert.Equal(2, exception.RowNumber);
    }

    [Theory]
    [InlineData(",Widget,cane", "product code must not be empty")]
    [InlineData("P1,,cane", "product name must not be empty")]
    [InlineData("P1,Widget,", "Category")]
    public async Task ReadProductsAsync_InvalidRow_ThrowsWithFileRowAndReason(string row, string expectedReason)
    {
        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => ReadProducts(productHeaderLine, row));

        Assert.Equal("products.csv", exception.FileName);
        Assert.Equal(2, exception.RowNumber);
        Assert.Contains(expectedReason, exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_MissingFile_ThrowsNamingTheFullPath()
    {
        var path = Path.Combine(TestRepository.NewTempDirectory(), "nope.csv");

        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(
            () => NormalizedSeedFileReader.ReadSuppliersAsync(path, Token));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadProductsAsync_MissingFile_ThrowsNamingTheFullPath()
    {
        var path = Path.Combine(TestRepository.NewTempDirectory(), "nope.csv");

        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(
            () => NormalizedSeedFileReader.ReadProductsAsync(path, Token));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_HeaderOnly_Throws()
    {
        var exception = await RejectedSupplier(supplierHeaderLine);

        Assert.Contains("no data rows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_EmptyFile_Throws()
    {
        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => ReadSuppliers());

        Assert.Contains("no header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_MissingColumn_ThrowsNamingTheColumn()
    {
        var exception = await RejectedSupplier("supplier_id,supplier_name,service_zips,product_categories,rating", "S1,A,02130,cane,7");

        Assert.Contains("can_mail_order", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadProductsAsync_HeaderOnly_Throws()
    {
        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => ReadProducts(productHeaderLine));

        Assert.Contains("no data rows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadProductsAsync_MissingColumn_Throws()
    {
        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => ReadProducts("product_code,product_name", "P1,Widget"));

        Assert.Contains("category", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_RowWithTooFewFields_ThrowsWithRow()
    {
        var exception = await RejectedSupplier(supplierHeaderLine, "S1,Acme,02130");

        Assert.Equal(2, exception.RowNumber);
    }

    [Fact]
    public async Task ReadSuppliersAsync_DuplicateSupplierId_NamesBothRows()
    {
        var exception = await RejectedSupplier(supplierHeaderLine, goodSupplierRow, "S2,B,02130,cane,7,y", goodSupplierRow);

        Assert.Equal(4, exception.RowNumber);
        Assert.Contains("row 2", exception.Reason, StringComparison.Ordinal);
        Assert.Contains("duplicate supplier_id", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_SupplierIdsDifferingOnlyInCase_AreDistinct()
    {
        var suppliers = await ReadSuppliers(supplierHeaderLine, "s1,A,02130,cane,7,y", "S1,B,02130,cane,7,y");

        Assert.Equal(2, suppliers.Count);
    }

    [Fact]
    public async Task ReadSuppliersAsync_SameStartZipTwiceInOneSupplier_Throws()
    {
        var exception = await RejectedSupplier(supplierHeaderLine, "S1,Acme,\"02130, 02130-02200\",cane,7,y");

        Assert.Equal(2, exception.RowNumber);
        Assert.Contains("02130", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadProductsAsync_CodesDifferingOnlyInCase_ThrowsNamingBothRows()
    {
        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(
            () => ReadProducts(productHeaderLine, "ab-1,A,cane", "AB-1,B,cane"));

        Assert.Equal(3, exception.RowNumber);
        Assert.Contains("row 2", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSuppliersAsync_UnpaddedCategoryWithSpace_IsLeftToTheDomain()
    {
        var suppliers = await ReadSuppliers(supplierHeaderLine, "S1,Acme,02130,\" CPAP\",7,y");

        Assert.Equal("cpap", Assert.Single(Assert.Single(suppliers).Categories).Value);
    }

    [Fact]
    public async Task ReadSuppliersAsync_EmptyRating_IsUnrated()
    {
        var suppliers = await ReadSuppliers(supplierHeaderLine, "S1,Acme,02130,cane,,n");

        Assert.Null(Assert.Single(suppliers).Rating);
    }
}

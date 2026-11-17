using OrderRouter.DataNormalizer.Products;
using OrderRouter.DataNormalizer.Reporting;

namespace OrderRouter.Tests.DataNormalizer.Products;

public class ProductNormalizerTests
{
    private const string header = "product_code,product_name,category";

    private static ProductNormalizationResult Normalize(string csv) => ProductNormalizer.Normalize(new StringReader(csv));

    [Fact]
    public void Normalize_ValidRows_KeepsSourceOrderAndLowerCasesCategory()
    {
        var result = Normalize($"{header}\r\nWC-1,Wheelchair,WheelChair\r\nCP-1,Cpap,CPAP\r\n");

        Assert.Equal(["WC-1", "CP-1"], result.Products.Select(product => product.Code));
        Assert.Equal("wheelchair", result.Products[0].Category.Value);
        Assert.Equal(2, result.Report.RowsWritten);
    }

    [Fact]
    public void Normalize_IdenticalDuplicate_KeepsOneAndReportsIt()
    {
        var result = Normalize($"{header}\nHB-MAN-019-BAS,Basic,hospital bed\nOTHER,Other,cane\nHB-MAN-019-BAS,Basic,hospital bed\n");

        Assert.Equal(2, result.Products.Count);
        Assert.Equal(3, result.Report.RowsRead);
        var duplicate = Assert.Single(result.Report.DuplicatesRemoved);
        Assert.Equal("HB-MAN-019-BAS", duplicate.Code);
        Assert.Equal([2, 4], duplicate.RowNumbers);
    }

    [Fact]
    public void Normalize_ThreeIdenticalCopies_ReportsOneDuplicateWithAllRows()
    {
        var result = Normalize($"{header}\nA,Name,cane\nA,Name,cane\nA,Name,cane\n");

        Assert.Single(result.Products);
        Assert.Equal([2, 3, 4], Assert.Single(result.Report.DuplicatesRemoved).RowNumbers);
    }

    [Fact]
    public void Normalize_DuplicateWithDifferentCategory_ThrowsNamingCodeAndRows()
    {
        var exception = Assert.Throws<NormalizationException>(
            () => Normalize($"{header}\nAB-1,Name,cane\nAB-1,Name,crutches\n"));

        Assert.Contains("AB-1", exception.Message);
        Assert.Contains("2", exception.Message);
        Assert.Contains("3", exception.Message);
    }

    [Fact]
    public void Normalize_DuplicateWithDifferentName_Throws()
    {
        var exception = Assert.Throws<NormalizationException>(
            () => Normalize($"{header}\nAB-1,Name One,cane\nAB-1,Name Two,cane\n"));

        Assert.Contains("AB-1", exception.Message);
    }

    [Fact]
    public void Normalize_DuplicateDifferingOnlyInCodeCaseAndWhitespace_IsOneProduct()
    {
        var result = Normalize($"{header}\nAB-1,Name,cane\n ab-1 , Name ,cane\n");

        var product = Assert.Single(result.Products);
        Assert.Equal("AB-1", product.Code);
        Assert.Single(result.Report.DuplicatesRemoved);
    }

    [Fact]
    public void Normalize_DuplicateDifferingOnlyInCategoryCase_IsIdentical()
    {
        var result = Normalize($"{header}\nAB-1,Name,CPAP\nAB-1,Name,cpap\n");

        Assert.Single(result.Products);
        Assert.Single(result.Report.DuplicatesRemoved);
    }

    [Theory]
    [InlineData(",Name,cane", "code")]
    [InlineData("AB-1,,cane", "name")]
    [InlineData("AB-1,Name,", "category")]
    [InlineData(",,", "category")]
    public void Normalize_InvalidRow_IsSkippedWithRowNumberAndReason(string badRow, string expectedReasonPart)
    {
        var result = Normalize($"{header}\nOK-1,Fine,cane\n{badRow}\n");

        var skipped = Assert.Single(result.Report.SkippedRows);
        Assert.Equal(3, skipped.RowNumber);
        Assert.Contains(expectedReasonPart, skipped.Reason);
        Assert.Single(result.Products);
    }

    [Fact]
    public void Normalize_MissingColumn_Throws()
    {
        var exception = Assert.Throws<NormalizationException>(() => Normalize("product_code,product_name\nA,B\n"));

        Assert.Contains("category", exception.Message);
    }

    [Fact]
    public void Normalize_EmptyBody_ReturnsEmptyCollections()
    {
        var result = Normalize($"{header}\n");

        Assert.Empty(result.Products);
        Assert.Empty(result.Report.SkippedRows);
        Assert.Empty(result.Report.DuplicatesRemoved);
    }
}

using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.DataNormalizer.Suppliers;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.DataNormalizer.Suppliers;

public class SupplierNormalizerTests
{
    private const string typoHeader = "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?";
    private const string fixedHeader = "supplier_id,supplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?";

    private static SupplierNormalizationResult Normalize(string csv) => SupplierNormalizer.Normalize(new StringReader(csv));

    [Theory]
    [InlineData(typoHeader)]
    [InlineData(fixedHeader)]
    public void Normalize_EitherNameSpelling_FillsName(string header)
    {
        var result = Normalize($"{header}\nSUP-001,Acme,\"2130, 2114-2212\",\"CPAP, cpap\",7,y\n");

        var supplier = Assert.Single(result.Suppliers);
        Assert.Equal("Acme", supplier.Name);
        Assert.Equal(1, result.Report.RowsWritten);
        Assert.True(supplier.Serves(ZipCode.Parse("02130")));
        Assert.True(supplier.Serves(ZipCode.Parse("02200")));
    }

    [Fact]
    public void Normalize_BothNameSpellings_ThrowsAmbiguous()
    {
        var csv = "supplier_id,supplier_name,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\n";

        var exception = Assert.Throws<NormalizationException>(() => Normalize(csv));

        Assert.Contains("ambiguous", exception.Message);
        Assert.Equal("suppliers.csv", exception.FileName);
    }

    [Fact]
    public void Normalize_MissingRequiredColumn_ThrowsNamingColumn()
    {
        var csv = "supplier_id,supplier_name,service_zips,product_categories,can_mail_order?\nSUP-001,A,02130,cpap,y\n";

        var exception = Assert.Throws<NormalizationException>(() => Normalize(csv));

        Assert.Contains("customer_satisfaction_score", exception.Message);
    }

    [Fact]
    public void Normalize_EmptyFile_Throws()
    {
        Assert.Throws<NormalizationException>(() => Normalize(string.Empty));
    }

    [Fact]
    public void Normalize_UnknownExtraColumn_IsIgnored()
    {
        var result = Normalize($"{fixedHeader},notes\nSUP-001,Acme,02130,cpap,7,y,hello\n");

        Assert.Single(result.Suppliers);
    }

    [Fact]
    public void Normalize_SupplierIds_AreKeptExactly()
    {
        var result = Normalize($"{fixedHeader}\nSUP-005,A,02130,cpap,7,y\nSUP-0810,B,02130,cpap,7,y\nSUP-T003,C,02130,cpap,7,y\n");

        Assert.Equal(["SUP-005", "SUP-0810", "SUP-T003"], result.Suppliers.Select(supplier => supplier.Id));
    }

    [Fact]
    public void Normalize_DuplicateSupplierId_ThrowsNamingIdAndRows()
    {
        var exception = Assert.Throws<NormalizationException>(
            () => Normalize($"{fixedHeader}\nSUP-001,A,02130,cpap,7,y\nSUP-001,B,02130,cpap,7,y\n"));

        Assert.Contains("SUP-001", exception.Message);
        Assert.Contains("2", exception.Message);
        Assert.Contains("3", exception.Message);
    }

    [Fact]
    public void Normalize_SameNameDifferentIds_AreDistinctSuppliers()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,Acme,02130,cpap,7,y\nSUP-002,Acme,02130,cpap,7,y\n");

        Assert.Equal(2, result.Suppliers.Count);
    }

    [Theory]
    [InlineData("SUP-002,B,02130,cpap,abc,y", "rating")]
    [InlineData("SUP-002,B,XYZ,cpap,7,y", "XYZ")]
    [InlineData("SUP-002,B,02130,cpap,11,y", "Rating")]
    [InlineData("SUP-002,B,02130,cpap,7,maybe", "can_mail_order")]
    [InlineData("SUP-002,B,02130,\"\",7,y", "category")]
    [InlineData("SUP-002,,02130,cpap,7,y", "name")]
    [InlineData("SUP-002,B,02130,cpap,0,y", "Rating")]
    public void Normalize_BadRow_IsSkippedWithRowNumberAndReason(string badRow, string expectedReasonPart)
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A,02130,cpap,7,y\n{badRow}\nSUP-003,C,02130,cpap,7,y\n");

        var skipped = Assert.Single(result.Report.SkippedRows);
        Assert.Equal(3, skipped.RowNumber);
        Assert.Contains(expectedReasonPart, skipped.Reason);
        Assert.Equal(["SUP-001", "SUP-003"], result.Suppliers.Select(supplier => supplier.Id));
        Assert.Equal(3, result.Report.RowsRead);
        Assert.Equal(2, result.Report.RowsWritten);
    }

    [Fact]
    public void Normalize_ShortRow_IsSkipped()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A\n");

        Assert.Empty(result.Suppliers);
        Assert.Single(result.Report.SkippedRows);
    }

    [Fact]
    public void Normalize_RowWithSeveralProblems_ReportsAllReasons()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A,XYZ,cpap,abc,maybe\n");

        var reason = Assert.Single(result.Report.SkippedRows).Reason;
        Assert.Contains("XYZ", reason);
        Assert.Contains("abc", reason);
        Assert.Contains("maybe", reason);
    }

    [Fact]
    public void Normalize_UnratedSupplier_HasNullRating()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A,02130,cpap,no ratings yet,n\n");

        var supplier = Assert.Single(result.Suppliers);
        Assert.Null(supplier.Rating);
        Assert.False(supplier.CanMailOrder);
    }

    [Fact]
    public void Normalize_MixedCaseCategories_CollapseToOne()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A,02130,\"CPAP, cpap, Cane\",7,y\n");

        var supplier = Assert.Single(result.Suppliers);
        Assert.Equal(2, supplier.Categories.Count);
        Assert.True(supplier.Handles(new Category("cpap")));
    }

    [Fact]
    public void Normalize_QuotedNameWithComma_ParsesCorrectly()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,\"Acme, Inc\",02130,cpap,7,y\n");

        Assert.Equal("Acme, Inc", Assert.Single(result.Suppliers).Name);
    }

    [Fact]
    public void Normalize_CrLfLineEndings_ParsesAllRows()
    {
        var result = Normalize($"{fixedHeader}\r\nSUP-001,A,02130,cpap,7,y\r\nSUP-002,B,02130,cpap,8,n\r\n");

        Assert.Equal(2, result.Suppliers.Count);
        Assert.Equal("SUP-002", result.Suppliers[1].Id);
    }

    [Fact]
    public void Normalize_BlankLineBetweenRows_KeepsPhysicalRowNumbers()
    {
        var result = Normalize($"{fixedHeader}\nSUP-001,A,02130,cpap,7,y\n\nSUP-002,B,XYZ,cpap,7,y\n");

        Assert.Equal(4, Assert.Single(result.Report.SkippedRows).RowNumber);
    }

    [Fact]
    public void Normalize_Collections_AreNeverNull()
    {
        var result = Normalize($"{fixedHeader}\n");

        Assert.Empty(result.Suppliers);
        Assert.Empty(result.Report.SkippedRows);
        Assert.Empty(result.Report.DuplicatesRemoved);
        Assert.Equal(0, result.Report.RowsRead);
    }
}

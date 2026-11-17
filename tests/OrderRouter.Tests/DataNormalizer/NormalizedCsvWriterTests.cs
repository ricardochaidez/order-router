using OrderRouter.DataNormalizer.Output;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Tests.DataNormalizer;

public class NormalizedCsvWriterTests
{
    private static Supplier CreateSupplier(string id, string[] ranges, string[] categories, decimal? rating, bool mail)
        => new(id, "Acme, Inc", categories.Select(category => new Category(category)), new ServiceArea(ranges.Select(ZipRange.Parse)), rating, mail);

    private static string WriteSuppliers(params Supplier[] suppliers)
    {
        using var writer = new StringWriter();
        NormalizedCsvWriter.WriteSuppliers(writer, suppliers);

        return writer.ToString();
    }

    [Fact]
    public void WriteSuppliers_NoSuppliers_WritesHeaderOnly()
    {
        Assert.Equal("supplier_id,supplier_name,service_zips,product_categories,rating,can_mail_order\n", WriteSuppliers());
    }

    [Fact]
    public void WriteSuppliers_SingleZipAndRange_UsesPaddedForms()
    {
        var text = WriteSuppliers(CreateSupplier("SUP-1", ["02130-02130", "02114-02212"], ["cane"], 7, true));

        Assert.Contains("\"02130, 02114-02212\"", text);
    }

    [Fact]
    public void WriteSuppliers_Categories_AreSortedAndLowerCase()
    {
        var text = WriteSuppliers(CreateSupplier("SUP-1", ["00100-99999"], ["CPAP", "cane", "Oxygen"], 7, true));

        Assert.Contains("\"cane, cpap, oxygen\"", text);
    }

    [Theory]
    [InlineData(10.0, "10")]
    [InlineData(6.7, "6.7")]
    [InlineData(9.245, "9.25")]
    public void WriteSuppliers_Rating_UsesInvariantShortFormat(double rating, string expected)
    {
        var text = WriteSuppliers(CreateSupplier("SUP-1", ["00100-99999"], ["cane"], (decimal)rating, true));

        Assert.Contains($",{expected},y\n", text);
    }

    [Fact]
    public void WriteSuppliers_UnratedAndNoMail_WritesEmptyRatingAndN()
    {
        var text = WriteSuppliers(CreateSupplier("SUP-1", ["00100-99999"], ["cane"], null, false));

        Assert.EndsWith(",cane,,n\n", text);
    }

    [Fact]
    public void WriteSuppliers_AnyInput_UsesLfOnly()
    {
        var text = WriteSuppliers(CreateSupplier("SUP-1", ["00100-99999"], ["cane"], 7, true));

        Assert.DoesNotContain('\r', text);
        Assert.Equal(2, text.Count(character => character == '\n'));
    }

    [Fact]
    public void WriteProducts_Products_WritesHeaderAndRows()
    {
        using var writer = new StringWriter();

        NormalizedCsvWriter.WriteProducts(writer, [new Product(" WC-1 ", " Chair ", new Category("WheelChair"))]);

        Assert.Equal("product_code,product_name,category\nWC-1,Chair,wheelchair\n", writer.ToString());
    }
}

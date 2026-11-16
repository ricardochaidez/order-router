using OrderRouter.Api.Routing;

namespace OrderRouter.UnitTests.Api.Routing;

public class ValidationErrorFormatterTests
{
    [Theory]
    [InlineData("CustomerZip", "customer_zip")]
    [InlineData("customer_zip", "customer_zip")]
    [InlineData("Items[0].Quantity", "items[0].quantity")]
    [InlineData("Items[12].ProductCode", "items[12].product_code")]
    [InlineData("OrderId", "order_id")]
    public void ToJsonPath_MemberPath_ReturnsSnakeCasePath(string key, string expected)
    {
        Assert.Equal(expected, ValidationErrorFormatter.ToJsonPath(key));
    }

    [Fact]
    public void Format_SingleError_PrefixesThePath()
    {
        var errors = ValidationErrorFormatter.Format([new("CustomerZip", ["must be exactly 5 digits."])]);

        Assert.Equal(["customer_zip must be exactly 5 digits."], errors);
    }

    [Fact]
    public void Format_ManyKeysInAnyOrder_ReturnsRequestOrder()
    {
        var errors = ValidationErrorFormatter.Format(
        [
            new("Items[1].Quantity", ["q1"]),
            new("Items[0].Quantity", ["q0"]),
            new("Items[0].ProductCode", ["p0"]),
            new("Items", ["items"]),
            new("CustomerZip", ["zip"]),
            new("OrderId", ["id"]),
        ]);

        Assert.Equal(
            ["order_id id", "customer_zip zip", "items items", "items[0].product_code p0", "items[0].quantity q0", "items[1].quantity q1"],
            errors);
    }

    [Fact]
    public void Format_SeveralMessagesForOneKey_ReturnsEachOne()
    {
        var errors = ValidationErrorFormatter.Format([new("Items", ["a", "b"])]);

        Assert.Equal(["items a", "items b"], errors);
    }

    [Fact]
    public void Format_UnknownKeys_SortAfterKnownOnes()
    {
        var errors = ValidationErrorFormatter.Format([new("Mystery", ["m"]), new("OrderId", ["id"])]);

        Assert.Equal(["order_id id", "mystery m"], errors);
    }

    [Fact]
    public void Format_NoErrors_ReturnsEmpty()
    {
        Assert.Empty(ValidationErrorFormatter.Format([]));
    }
}

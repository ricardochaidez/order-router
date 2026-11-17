using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Models;

public class RouteOrderCommandTests
{
    private static readonly RouteOrderLine[] validLines = [new("WC-STD-001", 2)];

    [Fact]
    public void Create_ValidValues_KeepsThemInOrder()
    {
        var command = RouteOrderCommand.Create("ORD-1", "02130", true, [new("A", 1), new("B", 3)]);

        Assert.Equal("ORD-1", command.OrderId);
        Assert.Equal("02130", command.CustomerZip.Value);
        Assert.True(command.MailOrder);
        Assert.Equal(["A", "B"], command.Lines.Select(line => line.ProductCode).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankOrderId_Throws(string orderId)
    {
        Assert.Throws<ArgumentException>(() => RouteOrderCommand.Create(orderId, "10015", false, validLines));
    }

    [Fact]
    public void Create_NoLines_Throws()
    {
        Assert.Throws<ArgumentException>(() => RouteOrderCommand.Create("ORD-1", "10015", false, []));
    }

    [Fact]
    public void Create_ExactlyMaxLines_Succeeds()
    {
        var lines = Enumerable.Range(1, RouteOrderCommand.MaxLines).Select(index => new RouteOrderLine($"P-{index}", 1)).ToArray();

        Assert.Equal(RouteOrderCommand.MaxLines, RouteOrderCommand.Create("ORD-1", "10015", false, lines).Lines.Count);
    }

    [Fact]
    public void Create_MoreThanMaxLines_Throws()
    {
        var lines = Enumerable.Range(1, RouteOrderCommand.MaxLines + 1).Select(index => new RouteOrderLine($"P-{index}", 1)).ToArray();

        Assert.Throws<ArgumentException>(() => RouteOrderCommand.Create("ORD-1", "10015", false, lines));
    }

    [Fact]
    public void Create_BlankProductCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => RouteOrderCommand.Create("ORD-1", "10015", false, [new(" ", 1)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_QuantityBelowOne_Throws(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteOrderCommand.Create("ORD-1", "10015", false, [new("A", quantity)]));
    }

    [Theory]
    [InlineData("2130")]
    [InlineData("1001a")]
    public void Create_BadZip_ThrowsDomainValidationException(string zip)
    {
        Assert.Throws<DomainValidationException>(() => RouteOrderCommand.Create("ORD-1", zip, false, validLines));
    }
}

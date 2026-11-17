using OrderRouter.DataNormalizer.Parsing;

namespace OrderRouter.Tests.DataNormalizer.Parsing;

public class MailOrderFlagParserTests
{
    [Theory]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData(" y ", true)]
    [InlineData("n", false)]
    [InlineData("N", false)]
    public void TryParse_YesOrNo_ReturnsFlag(string cell, bool expected)
    {
        var success = MailOrderFlagParser.TryParse(cell, out var flag, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(expected, flag);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1")]
    public void TryParse_AnythingElse_ReturnsError(string? cell)
    {
        var success = MailOrderFlagParser.TryParse(cell, out var flag, out var error);

        Assert.False(success);
        Assert.NotEmpty(error!);
        Assert.False(flag);
    }
}

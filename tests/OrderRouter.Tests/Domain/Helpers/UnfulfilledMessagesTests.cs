using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Tests.Domain.Helpers;

public class UnfulfilledMessagesTests
{
    // A stored order needs a message for every reason code routing can produce, or storing it throws.
    public static TheoryData<string> ReasonCodes =>
    [
        UnfulfilledLine.UnknownProductCode,
        UnfulfilledLine.TooManyCategories,
        .. Enum.GetValues<UnfulfillableReason>().Select(reason => reason.Code()),
    ];

    [Theory]
    [MemberData(nameof(ReasonCodes))]
    public void For_EveryReasonCode_HasAMessage(string reasonCode)
    {
        var message = UnfulfilledMessages.For(new UnfulfilledLine("P-1", 1, new Category("cane"), reasonCode));

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void For_TooManyCategories_NamesTheLimit()
    {
        var message = UnfulfilledMessages.For(new UnfulfilledLine("P-1", 1, new Category("cane"), UnfulfilledLine.TooManyCategories));

        Assert.Contains($"more than {PlanSearchRequest.MaxCategories} product categories", message);
    }

    [Fact]
    public void For_UnknownReasonCode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnfulfilledMessages.For(new UnfulfilledLine("P-1", 1, null, "MADE_UP")));
    }
}

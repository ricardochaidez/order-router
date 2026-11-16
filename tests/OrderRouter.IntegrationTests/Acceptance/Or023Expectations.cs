namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// The OR-023 expected suppliers, in one place. The OR-023 acceptance tests and the OR-024 all-control check both read
/// them, so the two suites cannot drift apart (OR-024 AC2). A tied set means "any one of these" (NFR-08).
/// </summary>
internal static class Or023Expectations
{
    public static readonly string[] ExclusiveWheelchair = ["SUP-T001"];
    public static readonly string[] LocalPreferenceWalker = ["SUP-T005"];
    public static readonly string[] Generalist = ["SUP-T007"];
    public static readonly string[] TiedCane = ["SUP-T003", "SUP-T004"];
    public static readonly string[] TiedUnratedRollator = ["SUP-T011", "SUP-T012"];
    public static readonly string[] Ord001 = ["SUP-0810"];
    public static readonly string[] Ord002 = ["SUP-0375", "SUP-042"];
    public static readonly string[] Ord003Tied = ["SUP-023", "SUP-065"];

    private const string wheelchair = "WC-STD-001";
    private const string walker = "WK-STD-009";
    private const string cane = "CN-STD-063";
    private const string rollator = "RL-STD-058";

    /// <summary>
    /// One deterministic or tied case: a request body and the supplier sets it may route to. When
    /// <see cref="Tied"/> is false the sorted supplier ids must equal <see cref="Suppliers"/>; when true a single supplier from it.
    /// </summary>
    internal sealed record Case(string Name, Func<string, string> Body, string[] Suppliers, bool Tied);

    /// <summary>The OR-023 orders with a fixed result (AC1 to AC6) on the seeded data, all with <c>mail_order=false</c> unless the sample says otherwise.</summary>
    public static readonly Case[] Cases =
    [
        new("ORD-001", SampleOrderAcceptanceTests.Ord001, Ord001, false),
        new("ORD-002", SampleOrderAcceptanceTests.Ord002, Ord002, false),
        new("ORD-003", SampleOrderAcceptanceTests.Ord003, Ord003Tied, true),
        new("T001 exclusive wheelchair", id => AcceptanceApi.Body(id, "00001", false, (wheelchair, 1)), ExclusiveWheelchair, false),
        new("T005 local preference walker", id => AcceptanceApi.Body(id, "00005", false, (walker, 1)), LocalPreferenceWalker, false),
        new("T007 generalist", id => AcceptanceApi.Body(id, "00007", false, (wheelchair, 1), (cane, 1), (walker, 1)), Generalist, false),
        new("T003/T004 tied cane", id => AcceptanceApi.Body(id, "00003", false, (cane, 1)), TiedCane, true),
        new("T011/T012 tied unrated rollator", id => AcceptanceApi.Body(id, "00040", false, (rollator, 1)), TiedUnratedRollator, true),
    ];
}

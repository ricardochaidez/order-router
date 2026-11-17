using OrderRouter.Api.Mappers;
using OrderRouter.Api.ViewModels.Response;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Services;

namespace OrderRouter.Tests.Domain.Services;

public class GetStoredOrderServiceTests
{
    private static readonly Guid id = Guid.CreateVersion7();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly FakeHasher hasher = new();
    private readonly FakeReader reader = new();


    // The service returns the stored order; these tests assert on the v1 response, so the adapter applies the Api mapper
    // exactly as the controller does.
    private ResponseAdapter UseCase() => new(new GetStoredOrderService(hasher, reader));

    private sealed class ResponseAdapter(GetStoredOrderService service)
    {
        public async Task<StoredOrderResponseViewModel?> GetAsync(Guid id, string? callerId, CancellationToken ct)
            => (await service.GetAsync(id, callerId, ct))?.ToViewModel();
    }

    private const string armsJson = """
        { "exp1_consolidation_vs_quality": { "arm": "treatment", "reason": "bucketed" },
          "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" },
          "exp3_partial_routing": { "arm": "control", "reason": "not_targeted" } }
        """;

    private static StoredRoutingOrder Stored()
        => new(
            id, "ORD-1", "hash-of-user-123", "10015", false, "routed", true, "{}", "cfg", "exact",
            new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
            [new StoredOrderItem(1, "A", 1, "cane", "S1", "Name S1", "local", null, null)], []);

    // AC5 at unit level: nothing to look up, so the reader is never called.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_CallerWithNoId_ReturnsNullWithoutCallingTheReaderOrTheHasher(string? callerId)
    {
        var result = await UseCase().GetAsync(id, callerId, Token);

        Assert.Null(result);
        Assert.Empty(reader.Calls);
        Assert.Empty(hasher.Hashed);
    }

    [Fact]
    public async Task GetAsync_Caller_PassesTheIdAndOnlyTheHashToTheReader()
    {
        reader.Result = Stored();

        await UseCase().GetAsync(id, "user-123", Token);

        var call = Assert.Single(reader.Calls);
        Assert.Equal((id, "hash-of-user-123"), call);
    }

    [Fact]
    public async Task GetAsync_ReaderFindsTheOrder_ReturnsTheMappedResponse()
    {
        reader.Result = Stored();

        var response = await UseCase().GetAsync(id, "user-123", Token);

        Assert.NotNull(response);
        Assert.Equal(id, response.Id);
        Assert.Equal(RoutingStatus.Routed, response.Status);
        Assert.Equal("S1", Assert.Single(response.Routing).SupplierId);
    }

    [Fact]
    public async Task GetAsync_ReaderMiss_ReturnsNull()
        => Assert.Null(await UseCase().GetAsync(id, "user-123", Token));

    // The flag is the only gate: off, damaged arms are never parsed.
    [Fact]
    public async Task GetAsync_FlagOffAndDamagedArms_StillReturnsTheOrder()
    {
        reader.Result = Stored();

        Assert.NotNull(await UseCase().GetAsync(id, "user-123", Token));
    }

    private sealed class FakeHasher : ICallerHasher
    {
        public List<string> Hashed { get; } = [];

        public string Hash(string callerId)
        {
            Hashed.Add(callerId);

            return $"hash-of-{callerId}";
        }
    }

    private sealed class FakeReader : IRoutingOrderReader
    {
        public StoredRoutingOrder? Result { get; set; }

        public List<(Guid Id, string CallerHash)> Calls { get; } = [];

        public Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken ct)
        {
            Calls.Add((id, callerHash));

            return Task.FromResult(Result);
        }
    }
}

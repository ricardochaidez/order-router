using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.UnitTests.Application.Routing;

public class GetStoredOrderUseCaseTests
{
    private static readonly Guid id = Guid.CreateVersion7();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly FakeHasher hasher = new();
    private readonly FakeReader reader = new();

    private GetStoredOrderUseCase UseCase() => new(hasher, reader);

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

        public Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken cancellationToken)
        {
            Calls.Add((id, callerHash));

            return Task.FromResult(Result);
        }
    }
}

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

    private bool exposeDiagnostics;

    // The service returns the stored order; these tests assert on the v1 response, so the adapter applies the Api mapper
    // exactly as the controller does.
    private ResponseAdapter UseCase() => new(new GetStoredOrderService(hasher, reader), exposeDiagnostics);

    private sealed class ResponseAdapter(GetStoredOrderService service, bool exposeDiagnostics)
    {
        public async Task<StoredOrderResponseViewModel?> GetAsync(Guid id, string? callerId, CancellationToken ct)
            => (await service.GetAsync(id, callerId, ct))?.ToViewModel(exposeDiagnostics);
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

    // AC1: off by default, so the field is absent.
    [Fact]
    public async Task GetAsync_FlagOffByDefault_HasNoDiagnostics()
    {
        reader.Result = Stored() with { ExperimentArmsJson = armsJson };

        var response = await UseCase().GetAsync(id, "user-123", Token);

        Assert.Null(response!.Diagnostics);
    }

    // AC3: the stored arms and search mode come back.
    [Fact]
    public async Task GetAsync_FlagOn_ShowsTheStoredArmsAndSearchMode()
    {
        exposeDiagnostics = true;
        reader.Result = Stored() with { ExperimentArmsJson = armsJson, SearchMode = "greedy" };

        var response = await UseCase().GetAsync(id, "user-123", Token);

        var diagnostics = Assert.IsType<DiagnosticsResponseViewModel>(response!.Diagnostics);
        Assert.Equal(new ExperimentDiagnosticResponseViewModel("treatment", "bucketed"), diagnostics.ExperimentArms["exp1_consolidation_vs_quality"]);
        Assert.Equal(new ExperimentDiagnosticResponseViewModel("control", "not_targeted"), diagnostics.ExperimentArms["exp3_partial_routing"]);
        Assert.Equal("greedy", diagnostics.SearchMode);
    }

    // AC3: POST and GET build diagnostics from the same stored record, so they are equal.
    [Fact]
    public async Task GetAsync_FlagOn_EqualsWhatPostBuildsFromTheSameStoredOrder()
    {
        exposeDiagnostics = true;
        var stored = Stored() with { ExperimentArmsJson = armsJson };
        reader.Result = stored;

        var response = await UseCase().GetAsync(id, "user-123", Token);

        var fromPost = DiagnosticsMappingExtensions.ToDiagnosticsViewModel(stored);
        Assert.Equal(fromPost.SearchMode, response!.Diagnostics!.SearchMode);
        Assert.Equal(fromPost.ExperimentArms, response.Diagnostics.ExperimentArms);
    }

    [Fact]
    public async Task GetAsync_FlagOnAndNoSearchRan_ShowsArmsWithoutSearchMode()
    {
        exposeDiagnostics = true;
        reader.Result = Stored() with { ExperimentArmsJson = armsJson, SearchMode = null };

        var response = await UseCase().GetAsync(id, "user-123", Token);

        Assert.Equal(3, response!.Diagnostics!.ExperimentArms.Count);
        Assert.Null(response.Diagnostics.SearchMode);
    }

    // A miss is a 404, so no diagnostics are built, even with damaged stored arms in play elsewhere.
    [Fact]
    public async Task GetAsync_FlagOnAndMiss_ReturnsNull()
    {
        exposeDiagnostics = true;

        Assert.Null(await UseCase().GetAsync(id, "user-123", Token));
    }

    // Fail safe: damaged stored arms are a fault, never a guessed arm.
    [Fact]
    public async Task GetAsync_FlagOnAndDamagedArms_Throws()
    {
        exposeDiagnostics = true;
        reader.Result = Stored();

        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase().GetAsync(id, "user-123", Token));
    }

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

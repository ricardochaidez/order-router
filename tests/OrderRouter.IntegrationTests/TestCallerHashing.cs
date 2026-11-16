namespace OrderRouter.IntegrationTests;

/// <summary>The caller-hash secret used by every test host. A test-only value, not a real secret (OR-027).</summary>
internal static class TestCallerHashing
{
    /// <summary>Set as <c>CallerHashing:Secret</c> on each test host.</summary>
    public const string Secret = "integration-tests-only-caller-hash-secret";
}

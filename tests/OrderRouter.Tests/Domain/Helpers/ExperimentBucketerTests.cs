using OrderRouter.Domain.Helpers;

namespace OrderRouter.Tests.Domain.Helpers;

public class ExperimentBucketerTests
{
    [Fact]
    public void Bucket_KnownAnswer_IsStableAcrossProcessesAndPlatforms()
    {
        // AC4: SHA-256("Exp2UnratedSuppliers:exp2:portal-client"), first 8 bytes big-endian, mod 100.
        // The constants were computed outside .NET (Python hashlib), so a platform-dependent read would be caught.
        Assert.Equal(97, ExperimentBucketer.Bucket("Exp2UnratedSuppliers", "exp2", "portal-client"));
        Assert.Equal(0, ExperimentBucketer.Bucket("Exp2UnratedSuppliers", "other-salt", "portal-client"));
        Assert.Equal(4, ExperimentBucketer.Bucket("Exp1ConsolidationVsQuality", "exp2", "portal-client"));
    }

    [Fact]
    public void Bucket_SameInput_SameBucket()
        => Assert.Equal(ExperimentBucketer.Bucket("e", "s", "u"), ExperimentBucketer.Bucket("e", "s", "u"));

    [Fact]
    public void Bucket_AlwaysWithinZeroToNinetyNine()
    {
        for (var i = 0; i < 2_000; i++)
        {
            Assert.InRange(ExperimentBucketer.Bucket("e", "s", $"unit-{i}"), 0, 99);
        }
    }

    [Fact]
    public void Bucket_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ExperimentBucketer.Bucket(null!, "s", "u"));
        Assert.Throws<ArgumentNullException>(() => ExperimentBucketer.Bucket("e", null!, "u"));
        Assert.Throws<ArgumentNullException>(() => ExperimentBucketer.Bucket("e", "s", null!));
    }
}

using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class CapabilityResolutionTests
{
    [Fact]
    public void Resolved_WithCategory_IsKnownAndReturnsTheCategory()
    {
        var result = CapabilityResolution.Resolved(new Category("oxygen"));

        Assert.True(result.IsKnown);
        Assert.True(result.TryGetCategory(out var category));
        Assert.Equal(new Category("oxygen"), category);
        Assert.Equal(new Category("oxygen"), result.Category);
    }

    [Fact]
    public void Unknown_Always_IsNotKnownAndHasNoCategory()
    {
        var result = CapabilityResolution.Unknown;

        Assert.False(result.IsKnown);
        Assert.False(result.TryGetCategory(out var category));
        Assert.Equal(default, category);
    }

    [Fact]
    public void DefaultInstance_Always_IsUnknown()
    {
        Assert.False(default(CapabilityResolution).IsKnown);
        Assert.Equal(CapabilityResolution.Unknown, default);
    }

    [Fact]
    public void Category_Unknown_ThrowsInvalidOperationException()
    {
        var result = CapabilityResolution.Unknown;

        Assert.Throws<InvalidOperationException>(() => result.Category);
    }

    [Fact]
    public void Equals_SameCategory_AreEqual()
    {
        Assert.Equal(CapabilityResolution.Resolved(new Category("oxygen")), CapabilityResolution.Resolved(new Category(" OXYGEN ")));
        Assert.NotEqual(CapabilityResolution.Resolved(new Category("oxygen")), CapabilityResolution.Resolved(new Category("cpap")));
        Assert.NotEqual(CapabilityResolution.Resolved(new Category("oxygen")), CapabilityResolution.Unknown);
    }
}

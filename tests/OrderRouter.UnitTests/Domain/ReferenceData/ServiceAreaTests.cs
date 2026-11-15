using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

public class ServiceAreaTests
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");

    private static ServiceArea Area(params string[] ranges) => new(ranges.Select(ZipRange.Parse));

    [Fact]
    public void Constructor_Empty_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => new ServiceArea([]));
        Assert.Throws<DomainValidationException>(() => new ServiceArea(null!));
    }

    [Fact]
    public void Contains_OverlappingSegments_MatchesAnySegment()
    {
        var area = Area("02000-02200", "02100-02300");

        Assert.True(area.Contains(ZipCode.Parse("02250")));
        Assert.True(area.Contains(ZipCode.Parse("02150")));
        Assert.False(area.Contains(ZipCode.Parse("02301")));
    }

    [Fact]
    public void IsExactly_SingleMatchingSegment_ReturnsTrue()
    {
        Assert.True(Area("00100-99999").IsExactly(nationwide));
    }

    [Theory]
    [InlineData("00100-99999", "00050")]
    [InlineData("00100-50000", "50001-99999")]
    [InlineData("00500-99999")]
    public void IsExactly_ExtraOrDifferentSegments_ReturnsFalse(params string[] ranges)
    {
        Assert.False(Area(ranges).IsExactly(nationwide));
    }

    [Fact]
    public void Segments_CallerChangesSourceList_AreNotAffected()
    {
        var source = new List<ZipRange> { nationwide };
        var area = new ServiceArea(source);

        source.Clear();

        Assert.Single(area.Segments);
    }
}

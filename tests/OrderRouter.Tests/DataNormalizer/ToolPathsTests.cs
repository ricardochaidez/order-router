using OrderRouter.DataNormalizer;

namespace OrderRouter.Tests.DataNormalizer;

public class ToolPathsTests
{
    [Fact]
    public void Resolve_NoArgs_UsesRepoDataAndSeedFolders()
    {
        var paths = ToolPaths.Resolve([], Path.Combine(TestRepository.Root, "tools"));

        Assert.Equal(TestRepository.DataDirectory, paths.InputDirectory);
        Assert.Equal(TestRepository.SeedDirectory, paths.OutputDirectory);
    }

    [Fact]
    public void Resolve_BothOverrides_DoesNotNeedTheRepoRoot()
    {
        var paths = ToolPaths.Resolve(["--input", "in", "--output", "out"], Path.GetTempPath());

        Assert.Equal(("in", "out"), (paths.InputDirectory, paths.OutputDirectory));
    }

    [Fact]
    public void Resolve_OnlyOutputOverride_KeepsDefaultInput()
    {
        var paths = ToolPaths.Resolve(["--output", "out"], TestRepository.Root);

        Assert.Equal(TestRepository.DataDirectory, paths.InputDirectory);
        Assert.Equal("out", paths.OutputDirectory);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--input")]
    public void Resolve_UnknownOrIncompleteArgument_Throws(string argument)
    {
        Assert.Throws<ArgumentException>(() => ToolPaths.Resolve([argument], TestRepository.Root));
    }

    [Fact]
    public void Resolve_NoRepoRootAndNoOverrides_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ToolPaths.Resolve([], Path.GetPathRoot(Path.GetTempPath())!));
    }
}

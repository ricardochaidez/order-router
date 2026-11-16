using OrderRouter.UnitTests.DataNormalizer;

namespace OrderRouter.UnitTests.Architecture;

/// <summary>Source guards: no real key is committed and no code turns on verbose token logging (SC-32, NFR-11).</summary>
public class SecretsConfigurationTests
{
    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void EnvExample_DevApiKey_IsADevOnlyPlaceholder()
    {
        var line = File.ReadAllLines(Path.Combine(TestRepository.Root, ".env.example"))
            .Single(entry => entry.StartsWith("DEV_API_KEY=", StringComparison.Ordinal));

        Assert.Contains("dev-only", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Gitignore_Always_ListsDotEnv()
        => Assert.Contains(".env", File.ReadAllLines(Path.Combine(TestRepository.Root, ".gitignore")).Select(line => line.Trim()));

    [Fact]
    public void AppSettingsFiles_Always_DoNotMentionTheDevApiKey()
    {
        var files = Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("DevApiKey", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_Always_DoesNotEnableVerboseTokenLogging()
        => Assert.All(
            SourceFiles(),
            file =>
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("ShowPII", text, StringComparison.Ordinal);
                Assert.DoesNotContain("LogCompleteSecurityArtifact", text, StringComparison.Ordinal);
            });
}

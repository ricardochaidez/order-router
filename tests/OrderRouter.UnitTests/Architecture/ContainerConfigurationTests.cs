using System.Text.RegularExpressions;
using OrderRouter.UnitTests.DataNormalizer;

namespace OrderRouter.UnitTests.Architecture;

/// <summary>Source guards for the container setup (OR-007): non-root, multi-stage, no data baked in, dev-only config.</summary>
public class ContainerConfigurationTests
{
    private static string[] DockerfileInstructions()
        => File.ReadAllLines(Path.Combine(TestRepository.Root, "Dockerfile"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

    [Fact]
    public void Dockerfile_Always_IsMultiStage()
        => Assert.True(DockerfileInstructions().Count(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)) >= 2);

    [Fact]
    public void Dockerfile_FinalStage_UsesTheAspNetRuntimeImage()
    {
        var lastFrom = DockerfileInstructions().Last(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("dotnet/aspnet:10.0", lastFrom, StringComparison.Ordinal);
    }

    [Fact]
    public void Dockerfile_AfterTheFinalFrom_SetsANonRootUser()
    {
        var instructions = DockerfileInstructions();
        var lastFrom = Array.FindLastIndex(instructions, line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        var user = instructions.Skip(lastFrom + 1).LastOrDefault(line => line.StartsWith("USER ", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(user);
        var name = user["USER ".Length..].Trim();
        Assert.NotEqual("root", name);
        Assert.NotEqual("0", name);
    }

    [Fact]
    public void Dockerfile_Always_DoesNotCopyDocsOrData()
        => Assert.DoesNotContain(
            DockerfileInstructions().Where(line => line.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase)),
            line => Regex.IsMatch(line, @"\bDocs\b", RegexOptions.IgnoreCase));

    [Fact]
    public void Compose_ApiService_RunsInDevelopmentWithoutMigrateOrSeed()
    {
        var compose = File.ReadAllText(Path.Combine(TestRepository.Root, "docker-compose.yml"));
        var api = compose[compose.IndexOf("\n  api:", StringComparison.Ordinal)..];
        api = api.Contains("\nvolumes:", StringComparison.Ordinal) ? api[..api.IndexOf("\nvolumes:", StringComparison.Ordinal)] : api;

        Assert.Contains("ASPNETCORE_ENVIRONMENT: Development", api, StringComparison.Ordinal);
        Assert.DoesNotContain("command:", api, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(migrate|seed)\b|\bdotnet ef\b", string.Join('\n', api.Split('\n').Where(line => !line.TrimStart().StartsWith('#'))));
    }

    [Fact]
    public void EnvExample_Always_ExistsAndMarksValuesDevOnly()
    {
        var path = Path.Combine(TestRepository.Root, ".env.example");

        Assert.True(File.Exists(path));
        var lines = File.ReadAllLines(path);
        Assert.Contains("DEV-ONLY", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("DEV_API_KEY=", StringComparison.Ordinal));
    }

    [Fact]
    public void Gitignore_Always_IgnoresDotEnv()
        => Assert.Contains(".env", File.ReadAllLines(Path.Combine(TestRepository.Root, ".gitignore")).Select(line => line.Trim()));
}

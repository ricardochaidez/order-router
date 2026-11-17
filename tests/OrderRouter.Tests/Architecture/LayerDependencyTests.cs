using System.Reflection;
using System.Xml.Linq;
using OrderRouter.Domain;
using OrderRouter.Infrastructure;

namespace OrderRouter.Tests.Architecture;

/// <summary>
/// Enforces the layer dependency rules (SERVICE-STRUCTURE.md): Domain depends on no project, Infrastructure only on
/// Domain, and Api on Domain and Infrastructure.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Dictionary<string, string[]> allowedReferences = new()
    {
        ["Domain"] = [],
        ["Infrastructure"] = ["Domain"],
        ["Api"] = ["Domain", "Infrastructure"],
    };

    private static readonly Dictionary<string, Assembly> layerAssemblies = new()
    {
        ["Domain"] = typeof(DomainAssemblyMarker).Assembly,
        ["Infrastructure"] = typeof(InfrastructureAssemblyMarker).Assembly,
        ["Api"] = typeof(Program).Assembly,
    };

    public static TheoryData<string> Layers => [.. allowedReferences.Keys];

    // Reads the .csproj files because it is the only way to prove the *required* references:
    // the compiler drops references to projects whose types are never used.
    [Theory]
    [MemberData(nameof(Layers))]
    public void ProjectReferences_ForLayer_MatchAllowedSet(string layer)
    {
        var csprojPath = Path.Combine(FindRepositoryRoot(), "src", $"OrderRouter.{layer}", $"OrderRouter.{layer}.csproj");

        var actual = XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(reference => (string)reference.Attribute("Include")!)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .Select(name => name.Replace("OrderRouter.", string.Empty, StringComparison.Ordinal))
            .Order()
            .ToArray();

        Assert.Equal(allowedReferences[layer].Order(), actual);
    }

    // Only covers *forbidden* references: the compiler omits references to unused projects,
    // so a required reference can't be proven from the compiled assembly.
    [Theory]
    [MemberData(nameof(Layers))]
    public void ReferencedAssemblies_ForLayer_ContainNoForbiddenLayer(string layer)
    {
        var referencedLayers = layerAssemblies[layer]
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("OrderRouter.", StringComparison.Ordinal))
            .Select(name => name["OrderRouter.".Length..])
            .Where(name => layerAssemblies.ContainsKey(name));

        var forbidden = referencedLayers.Except(allowedReferences[layer]).ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void ProjectReferences_ForDataNormalizerTool_AreDomainOnly()
    {
        var csprojPath = Path.Combine(FindRepositoryRoot(), "tools", "OrderRouter.DataNormalizer", "OrderRouter.DataNormalizer.csproj");

        Assert.Equal(["OrderRouter.Domain"], ReadProjectReferenceNames(csprojPath));
    }

    // The tool is an offline utility; if a src project referenced it, it would ship with the API.
    [Theory]
    [MemberData(nameof(Layers))]
    public void ProjectReferences_ForLayer_DoNotIncludeDataNormalizerTool(string layer)
    {
        var csprojPath = Path.Combine(FindRepositoryRoot(), "src", $"OrderRouter.{layer}", $"OrderRouter.{layer}.csproj");

        Assert.DoesNotContain("OrderRouter.DataNormalizer", ReadProjectReferenceNames(csprojPath));
    }

    // Keeps EF Core / Npgsql (and any other heavy library) out of the domain for good (ADR-0002). Only the two small,
    // framework-agnostic Microsoft.Extensions abstractions are allowed: options (Sections/) and logging (services).
    [Fact]
    public void PackageReferences_ForDomain_AreOnlyFrameworkAgnosticAbstractions()
    {
        var csprojPath = Path.Combine(FindRepositoryRoot(), "src", "OrderRouter.Domain", "OrderRouter.Domain.csproj");

        var packages = XDocument.Load(csprojPath)
            .Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .ToArray();

        Assert.Equal(
            ["Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Options"],
            packages.Order().ToArray());
    }

    private static string[] ReadProjectReferenceNames(string csprojPath)
        => [.. XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(reference => (string)reference.Attribute("Include")!)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .Order()];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrderRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find OrderRouter.slnx above the test output directory.");
    }
}

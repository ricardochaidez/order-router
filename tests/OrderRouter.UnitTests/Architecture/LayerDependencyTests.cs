using System.Reflection;
using System.Xml.Linq;
using OrderRouter.Application;
using OrderRouter.Domain;
using OrderRouter.Infrastructure;

namespace OrderRouter.UnitTests.Architecture;

/// <summary>
/// Enforces the layer dependency rules: Domain depends on nothing, Application on Domain,
/// Infrastructure on Domain and Application, and Api on Application and Infrastructure.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Dictionary<string, string[]> allowedReferences = new()
    {
        ["Domain"] = [],
        ["Application"] = ["Domain"],
        ["Infrastructure"] = ["Domain", "Application"],
        ["Api"] = ["Application", "Infrastructure"],
    };

    private static readonly Dictionary<string, Assembly> layerAssemblies = new()
    {
        ["Domain"] = typeof(DomainAssemblyMarker).Assembly,
        ["Application"] = typeof(ApplicationAssemblyMarker).Assembly,
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

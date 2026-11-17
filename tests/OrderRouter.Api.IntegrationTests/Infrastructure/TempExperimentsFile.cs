using Microsoft.Extensions.FileProviders;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A temp folder holding an <c>experiments.json</c> that a test host loads with <c>AddJsonFile</c> and that can be
/// rewritten to prove hot reload. Shared by the OR-017 host tests and the OR-024 matrix so there is one way to pin arms.
/// </summary>
internal sealed class TempExperimentsFile : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("experiments-").FullName;

    /// <summary>Writes <paramref name="json"/> as the initial file.</summary>
    public TempExperimentsFile(string json) => Write(json);

    /// <summary>A file provider over the temp folder.</summary>
    public IFileProvider Provider => new PhysicalFileProvider(directory);

    /// <summary>Replaces the file content.</summary>
    public void Write(string json) => File.WriteAllText(Path.Combine(directory, "experiments.json"), json);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);
}

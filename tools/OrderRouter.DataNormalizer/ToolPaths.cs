namespace OrderRouter.DataNormalizer;

/// <summary>Where the tool reads from and writes to.</summary>
/// <param name="InputDirectory">Folder with the raw CSV files.</param>
/// <param name="OutputDirectory">Folder for the normalized files.</param>
public sealed record ToolPaths(string InputDirectory, string OutputDirectory)
{
    private const string solutionFile = "OrderRouter.slnx";

    /// <summary>
    /// Resolves the paths (AC14). Defaults to <c>Docs/Data</c> and <c>Docs/Data/Seed</c> under the repo root,
    /// found by walking up to <c>OrderRouter.slnx</c>; <c>--input</c> and <c>--output</c> override them.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="currentDirectory">Where to start looking for the repo root.</param>
    /// <exception cref="ArgumentException">An unknown argument, or an option without a value.</exception>
    /// <exception cref="InvalidOperationException">No override was given and the repo root was not found.</exception>
    public static ToolPaths Resolve(string[] args, string currentDirectory)
    {
        string? input = null;
        string? output = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input" when index + 1 < args.Length:
                    input = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument \"{args[index]}\". Use --input <dir> and --output <dir>.");
            }
        }

        if (input is not null && output is not null)
        {
            return new ToolPaths(input, output);
        }

        var root = FindRepositoryRoot(currentDirectory);
        var dataDirectory = Path.Combine(root, "Docs", "Data");

        return new ToolPaths(input ?? dataDirectory, output ?? Path.Combine(dataDirectory, "Seed"));
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, solutionFile)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not find {solutionFile} above \"{start}\"; pass --input and --output.");
    }
}

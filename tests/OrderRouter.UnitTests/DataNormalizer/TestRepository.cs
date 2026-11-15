namespace OrderRouter.UnitTests.DataNormalizer;

/// <summary>Locates the real data files and creates throw-away folders for runner tests.</summary>
internal static class TestRepository
{
    public static string Root { get; } = FindRoot();

    public static string DataDirectory => Path.Combine(Root, "Docs", "Data");

    public static string SeedDirectory => Path.Combine(DataDirectory, "Seed");

    public static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "order-router-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        return path;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrderRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find OrderRouter.slnx.");
    }
}

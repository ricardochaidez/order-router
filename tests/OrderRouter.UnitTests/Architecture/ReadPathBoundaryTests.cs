using OrderRouter.UnitTests.DataNormalizer;

namespace OrderRouter.UnitTests.Architecture;

/// <summary>Guards AC4: the OR-006 read-path files never write and never track entities.</summary>
public class ReadPathBoundaryTests
{
    private static readonly string[] readPathFiles = ["EfProductCatalog.cs", "EfReferenceDataReader.cs", Path.Combine("Mappers", "SupplierRowMapper.cs")];

    private static readonly string[] forbidden =
        ["SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", ".Add(", ".Update(", ".Remove(", ".Attach("];

    private static string Read(string file)
        => File.ReadAllText(Path.Combine(
            TestRepository.Root, "src", "OrderRouter.Infrastructure", "Persistence", "ReferenceData", file));

    [Theory]
    [MemberData(nameof(Files))]
    public void ReadPathFile_Always_ContainsNoWriteApi(string file)
    {
        var source = Read(file);

        Assert.DoesNotContain(forbidden, token => source.Contains(token, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EfProductCatalog.cs")]
    [InlineData("EfReferenceDataReader.cs")]
    public void ReadPathFile_EveryDbSetQuery_UsesAsNoTracking(string file)
    {
        var source = Read(file);
        var queries = source.Split("context.", StringSplitOptions.None).Skip(1).ToArray();

        Assert.NotEmpty(queries);
        Assert.All(queries, query => Assert.Contains("AsNoTracking()", query[..Math.Min(query.Length, 80)], StringComparison.Ordinal));
    }

    public static TheoryData<string> Files() => new(readPathFiles);
}

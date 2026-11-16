using Microsoft.Extensions.Configuration;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Infrastructure.Experiments;
using OrderRouter.Infrastructure.Experiments.Mappers;

namespace OrderRouter.UnitTests.Infrastructure.Experiments;

public class ShippedExperimentsFileTests
{
    private static string ShippedFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrderRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "OrderRouter.Api", "experiments.json");
    }

    [Fact]
    public void ShippedFile_IsValidAndEveryExperimentIsDisabled()
    {
        // AC1, AB-03
        var root = new ConfigurationBuilder().AddJsonFile(ShippedFile(), optional: false).Build();
        var options = root.Get<ExperimentsOptions>()!;

        Assert.True(ExperimentsOptionsMapper.TryBuild(options, out var configuration, out var errors), string.Join("; ", errors));

        Assert.False(configuration.Exp1ConsolidationVsQuality.Enabled);
        Assert.False(configuration.Exp2UnratedSuppliers.Enabled);
        Assert.False(configuration.Exp3PartialRouting.Enabled);
        Assert.All(
            new[] { configuration.Exp1ConsolidationVsQuality, configuration.Exp2UnratedSuppliers, configuration.Exp3PartialRouting },
            definition => Assert.Equal(0, definition.TreatmentPercentage));
        Assert.Equal(RoutingParameters.Default, configuration.Parameters);
    }
}

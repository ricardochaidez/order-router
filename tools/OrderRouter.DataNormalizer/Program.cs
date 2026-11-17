using Microsoft.Extensions.Logging;
using OrderRouter.DataNormalizer.Reporting;
using OrderRouter.DataNormalizer;

// Offline tool: regenerates Docs/Data/Seed from the raw CSVs. Exit code 0 on success, 1 on a fatal error.
using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
var logger = loggerFactory.CreateLogger("DataNormalizer");

try
{
    var paths = ToolPaths.Resolve(args, Directory.GetCurrentDirectory());
    var result = NormalizationRunner.Run(paths.InputDirectory, paths.OutputDirectory);

    NormalizationReportLogger.Log(logger, result.Suppliers);
    NormalizationReportLogger.Log(logger, result.Products);
    logger.LogInformation("Normalized files written to {Output}", paths.OutputDirectory);

    return 0;
}
catch (Exception exception) when (exception is NormalizationException or ArgumentException or InvalidOperationException or IOException)
{
    logger.LogError("Normalization failed, nothing was written: {Message}", exception.Message);

    return 1;
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using OrderRouter.Domain.Entities.Experiments;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// The <c>config_version</c> of an accepted snapshot (ADR-0005, AB-04): the first 12 lowercase hex characters of the
/// SHA-256 of a canonical JSON built from the <i>parsed</i> values in a fixed property order. A whitespace-only or
/// formatting-only edit therefore keeps the version, and any value change alters it.
/// </summary>
public static class ConfigVersion
{
    private const int hexLength = 12;

    /// <summary>Computes the version.</summary>
    /// <param name="exp1">EXP-1 definition.</param>
    /// <param name="exp2">EXP-2 definition.</param>
    /// <param name="exp3">EXP-3 definition.</param>
    /// <param name="parameters">The parameters carried to every assignment.</param>
    public static string Compute(ExperimentDefinition exp1, ExperimentDefinition exp2, ExperimentDefinition exp3, RoutingParameters parameters)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("experiments");
            Write(writer, exp1);
            Write(writer, exp2);
            Write(writer, exp3);
            writer.WriteEndArray();
            writer.WriteStartObject("parameters");
            writer.WriteNumber("similarityBand", Normalize(parameters.SimilarityBand));
            writer.WriteNumber("qualityUpliftThreshold", Normalize(parameters.QualityUpliftThreshold));
            writer.WriteNumber("unratedPrior", Normalize(parameters.UnratedPrior));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()))[..hexLength];
    }

    private static void Write(Utf8JsonWriter writer, ExperimentDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("id", definition.Id);
        writer.WriteBoolean("enabled", definition.Enabled);
        writer.WriteNumber("treatmentPercentage", definition.TreatmentPercentage);
        writer.WriteString("salt", definition.Salt);
        writer.WriteStartObject("targeting");
        WriteStrings(writer, "callers", definition.Targeting.Callers);
        WriteStrings(writer, "zipPrefixes", definition.Targeting.ZipPrefixes);
        WriteStrings(writer, "zipRanges", definition.Targeting.ZipRanges.Select(range => range.ToString()));
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    // 2.0m and 2m are the same value; dividing by 1.000...m drops trailing zeros so both write the same digits.
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}

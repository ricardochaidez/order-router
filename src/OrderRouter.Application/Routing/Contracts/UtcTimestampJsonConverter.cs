using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderRouter.Application.Routing.Contracts;

/// <summary>
/// Writes a timestamp as ISO 8601 UTC with a trailing <c>Z</c> (requirements §9.5), for example
/// <c>2026-11-15T10:00:00Z</c>. The default writer would emit <c>+00:00</c> for a value read from PostgreSQL.
/// Reading accepts any ISO 8601 offset.
/// </summary>
public sealed class UtcTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.UtcDateTime.ToString(format, CultureInfo.InvariantCulture));
}

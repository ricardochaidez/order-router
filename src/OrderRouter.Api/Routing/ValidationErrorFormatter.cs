using System.Text.Json;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Turns the framework's validation errors (keyed by member path) into envelope messages that name the snake_case
/// JSON path, in a stable request order (ADR-0004, AC1).
/// </summary>
internal static class ValidationErrorFormatter
{
    // Declaration order of the request, used to sort errors the same way on every call.
    private static readonly string[] knownNames = ["order_id", "customer_zip", "mail_order", "items", "product_code", "quantity"];

    /// <summary>Formats every error, one message per rule that failed.</summary>
    /// <param name="errors">Member path to messages, as produced by the validation filter.</param>
    public static IReadOnlyList<string> Format(IEnumerable<KeyValuePair<string, string[]>> errors)
        => [.. errors
            .Select(entry => (Segments: Parse(entry.Key), Messages: entry.Value))
            .OrderBy(entry => entry.Segments, SegmentComparer.Instance)
            .SelectMany(entry => entry.Messages.Select(message => $"{Render(entry.Segments)} {message}".TrimStart()))];

    /// <summary>Converts a member path such as <c>Items[0].Quantity</c> to <c>items[0].quantity</c>.</summary>
    /// <param name="key">The member path.</param>
    public static string ToJsonPath(string key) => Render(Parse(key));

    private static string Render(IReadOnlyList<Segment> segments)
        => string.Join('.', segments.Select(segment => segment.Index is { } index ? $"{segment.Name}[{index}]" : segment.Name));

    private static List<Segment> Parse(string key)
    {
        var segments = new List<Segment>();

        foreach (var part in key.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            var name = bracket < 0 ? part : part[..bracket];
            int? index = null;

            if (bracket >= 0 && int.TryParse(part[(bracket + 1)..].TrimEnd(']'), out var parsed))
            {
                index = parsed;
            }

            segments.Add(new Segment(JsonNamingPolicy.SnakeCaseLower.ConvertName(name), index));
        }

        return segments;
    }

    private readonly record struct Segment(string Name, int? Index);

    private sealed class SegmentComparer : IComparer<List<Segment>>
    {
        public static SegmentComparer Instance { get; } = new();

        public int Compare(List<Segment>? x, List<Segment>? y)
        {
            for (var position = 0; position < Math.Min(x!.Count, y!.Count); position++)
            {
                var byName = Rank(x[position].Name).CompareTo(Rank(y[position].Name));

                if (byName != 0)
                {
                    return byName;
                }

                var byIndex = (x[position].Index ?? -1).CompareTo(y[position].Index ?? -1);

                if (byIndex != 0)
                {
                    return byIndex;
                }
            }

            return x.Count.CompareTo(y.Count);
        }

        // Unknown names sort after the known ones, so the order stays deterministic.
        private static int Rank(string name)
        {
            var rank = Array.IndexOf(knownNames, name);

            return rank < 0 ? knownNames.Length : rank;
        }
    }
}

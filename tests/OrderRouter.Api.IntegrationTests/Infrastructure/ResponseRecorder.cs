using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Writes every recorded request and response to disk so a test run can be analysed afterwards without re-running it.
/// It does nothing unless the <c>ORDERROUTER_RESULTS_DIR</c> environment variable is set, so a plain
/// <c>dotnet test</c> is unaffected. <c>scripts/run-tests.sh</c> sets it.
/// </summary>
/// <remarks>
/// Each record becomes <c>&lt;dir&gt;/responses/&lt;group&gt;/&lt;name&gt;.json</c>. A <c>summary.md</c> table of every record is
/// rewritten after each one, so it is complete even if the run is stopped early.
/// </remarks>
internal static class ResponseRecorder
{
    /// <summary>The environment variable that turns recording on and names the output folder.</summary>
    public const string DirectoryVariable = "ORDERROUTER_RESULTS_DIR";

    private static readonly Lock gate = new();
    private static readonly List<Row> rows = [];
    private static readonly JsonSerializerOptions indented = new() { WriteIndented = true };
    private static int sequence;

    private sealed record Row(string Group, string Name, int Status, string? OrderStatus, bool? Feasible, string Suppliers, string Arms, string File);

    /// <summary>The output folder, or <c>null</c> when recording is off.</summary>
    private static string? Directory
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(DirectoryVariable);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>Records one exchange. A no-op when recording is off.</summary>
    /// <param name="group">Folder under <c>responses</c>, such as <c>default-route</c> or <c>experiments/exp1</c>.</param>
    /// <param name="name">File name without extension.</param>
    /// <param name="description">What the scenario shows, for the reader of the file.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Request path.</param>
    /// <param name="requestBody">Request body, or <c>null</c>.</param>
    /// <param name="statusCode">HTTP status of the reply.</param>
    /// <param name="location">The <c>Location</c> header of the reply, if any.</param>
    /// <param name="responseText">Raw response body.</param>
    /// <param name="experiments">The <c>experiments.json</c> the host ran with, if the scenario pinned one.</param>
    public static void Record(string group, string name, string description, string method, string path, string? requestBody, int statusCode, string? location, string responseText, string? experiments = null)
    {
        var root = Directory;

        if (root is null)
        {
            return;
        }

        var response = TryParse(responseText);
        var document = new
        {
            scenario = name,
            group,
            description,
            experiments_config = experiments is null ? null : TryParse(experiments),
            request = new { method, path, body = requestBody is null ? null : TryParse(requestBody) },
            response = new { status = statusCode, location, body = response ?? (object?)responseText },
        };

        var fileName = Sanitize(name) + ".json";
        var relative = Path.Combine("responses", group.Replace('/', Path.DirectorySeparatorChar), fileName);
        var full = Path.Combine(root, relative);

        lock (gate)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // The same name twice (a theory row or a repeated call) must not overwrite the first response.
            if (File.Exists(full))
            {
                relative = Path.Combine(Path.GetDirectoryName(relative)!, $"{Path.GetFileNameWithoutExtension(fileName)}-{++sequence}.json");
                full = Path.Combine(root, relative);
            }

            File.WriteAllText(full, JsonSerializer.Serialize(document, indented));
            rows.Add(Summarize(group, name, statusCode, response, relative));
            WriteSummary(root);
        }
    }

    /// <summary>A file-system safe name for a test display name.</summary>
    public static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? character : '_');
        }

        var text = builder.ToString().Trim('_');

        return text.Length > 120 ? text[..120] : text.Length == 0 ? "unnamed" : text;
    }

    private static JsonElement? TryParse(string text)
    {
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Row Summarize(string group, string name, int status, JsonElement? body, string file)
    {
        string? orderStatus = null;
        bool? feasible = null;
        var suppliers = string.Empty;
        var arms = string.Empty;

        if (body is { ValueKind: JsonValueKind.Object } json)
        {
            orderStatus = json.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            feasible = json.TryGetProperty("feasible", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False ? f.GetBoolean() : null;

            if (json.TryGetProperty("routing", out var routing) && routing.ValueKind == JsonValueKind.Array)
            {
                suppliers = string.Join(" + ", routing.EnumerateArray().Select(group => group.TryGetProperty("supplier_id", out var id) ? id.GetString() : "?"));
            }

            if (json.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.TryGetProperty("experiment_arms", out var experimentArms))
            {
                arms = string.Join(", ", experimentArms.EnumerateObject().Select(arm => $"{arm.Name[..4]}={arm.Value.GetProperty("arm").GetString()}"));
            }
        }

        return new Row(group, name, status, orderStatus, feasible, suppliers, arms, file);
    }

    private static void WriteSummary(string root)
    {
        var text = new StringBuilder()
            .AppendLine("# Response summary")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"{rows.Count} recorded responses. Open the linked file for the full request and response.")
            .AppendLine()
            .AppendLine("| Group | Scenario | HTTP | Order status | Feasible | Suppliers | Experiment arms | File |")
            .AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var row in rows.OrderBy(row => row.Group, StringComparer.Ordinal).ThenBy(row => row.Name, StringComparer.Ordinal))
        {
            var file = row.File.Replace(Path.DirectorySeparatorChar, '/');
            text.AppendLine(CultureInfo.InvariantCulture, $"| {row.Group} | {row.Name.Replace("|", "\\|", StringComparison.Ordinal)} | {row.Status} | {row.OrderStatus} | {row.Feasible} | {row.Suppliers} | {row.Arms} | [{Path.GetFileName(file)}]({file}) |");
        }

        File.WriteAllText(Path.Combine(root, "summary.md"), text.ToString());
    }
}

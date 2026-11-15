namespace OrderRouter.DataNormalizer.Reporting;

/// <summary>
/// A fatal, file-level failure: a missing or ambiguous column, a conflicting duplicate product or a
/// duplicate supplier id. Unlike a bad row it cannot be skipped, so the whole run stops and nothing is
/// written (NFR-10: fail safe, never guess).
/// </summary>
public sealed class NormalizationException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="fileName">The file being normalized.</param>
    /// <param name="message">The cause; the final message is prefixed with the file name.</param>
    public NormalizationException(string fileName, string message)
        : base($"{fileName}: {message}")
    {
        FileName = fileName;
    }

    /// <summary>The file being normalized when the failure happened.</summary>
    public string FileName { get; }
}

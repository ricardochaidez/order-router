namespace OrderRouter.Infrastructure.Persistence.Seeding;

/// <summary>
/// The seed cannot proceed: a file is missing or malformed, or a row was rejected (FR-04, NFR-10).
/// The message always names the file, the row (when there is one) and the reason, so the engineer
/// can fix the data. The seeder never repairs or skips input.
/// </summary>
public sealed class ReferenceDataSeedException : Exception
{
    /// <summary>Initializes a new instance with no details.</summary>
    public ReferenceDataSeedException()
        : this("unknown", null, "Reference data seed failed.")
    {
    }

    /// <summary>Initializes a new instance with a message only.</summary>
    /// <param name="message">What went wrong.</param>
    public ReferenceDataSeedException(string message)
        : this("unknown", null, message)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ReferenceDataSeedException(string message, Exception innerException)
        : this("unknown", null, message, innerException)
    {
    }

    /// <summary>Initializes a new instance that names the file, row and reason.</summary>
    /// <param name="fileName">File name (or full path when the file is missing).</param>
    /// <param name="rowNumber">Physical row, where the header is row 1; <c>null</c> for a whole-file problem.</param>
    /// <param name="reason">Why the file or row was rejected.</param>
    /// <param name="innerException">The underlying cause, when there is one.</param>
    public ReferenceDataSeedException(string fileName, int? rowNumber, string reason, Exception? innerException = null)
        : base(rowNumber is { } row ? $"{fileName} row {row}: {reason}" : $"{fileName}: {reason}", innerException)
    {
        FileName = fileName;
        RowNumber = rowNumber;
        Reason = reason;
    }

    /// <summary>The file that was rejected.</summary>
    public string FileName { get; } = "unknown";

    /// <summary>The rejected row (header = row 1), or <c>null</c> when the whole file is the problem.</summary>
    public int? RowNumber { get; }

    /// <summary>Why the file or row was rejected.</summary>
    public string Reason { get; } = string.Empty;
}

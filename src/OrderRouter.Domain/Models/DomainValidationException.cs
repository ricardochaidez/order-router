namespace OrderRouter.Domain.Models;

/// <summary>
/// Thrown when a domain invariant is broken, for example a ZIP code that is not 5 digits.
/// The message always names the field and the rejected value so callers that skip bad rows
/// (OR-003) or fail a seed (OR-005) can log a clear reason. The domain never repairs input (NFR-10).
/// </summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public DomainValidationException()
    {
    }

    /// <summary>Initializes a new instance with a message that names the field and value.</summary>
    /// <param name="message">What was invalid and why.</param>
    public DomainValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">What was invalid and why.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

using System.Diagnostics.CodeAnalysis;

namespace OrderRouter.DataNormalizer.Parsing;

/// <summary>Parses the <c>can_mail_order?</c> flag (DQ-08).</summary>
public static class MailOrderFlagParser
{
    /// <summary>Parses <c>y</c> or <c>n</c>, case-insensitive and trimmed.</summary>
    /// <param name="cell">The raw cell text.</param>
    /// <param name="canMailOrder">The flag; <c>false</c> on failure.</param>
    /// <param name="error">Why the cell was rejected; <c>null</c> on success.</param>
    /// <returns><c>true</c> when the cell is <c>y</c> or <c>n</c>; anything else is rejected, never guessed.</returns>
    public static bool TryParse(string? cell, out bool canMailOrder, [NotNullWhen(false)] out string? error)
    {
        canMailOrder = false;
        error = null;

        switch (cell?.Trim().ToLowerInvariant())
        {
            case "y":
                canMailOrder = true;
                return true;
            case "n":
                return true;
            default:
                error = $"can_mail_order \"{cell}\" must be y or n";
                return false;
        }
    }
}

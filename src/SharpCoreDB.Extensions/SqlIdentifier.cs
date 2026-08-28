using System.Text.RegularExpressions;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Validates SQL identifiers (table/column names) that must be interpolated into SQL
/// statements. Identifiers cannot be bound as query parameters, so the accepted
/// remediation for S2077 is to whitelist the characters they may contain.
/// </summary>
internal static class SqlIdentifier
{
    private static readonly Regex ValidIdentifier = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Ensures the value is a safe SQL identifier; otherwise throws.
    /// </summary>
    public static string EnsureSafe(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!ValidIdentifier.IsMatch(value))
        {
            throw new ArgumentException(
                $"'{paramName}' must be a valid SQL identifier (letters, digits, underscore).",
                paramName);
        }

        return value;
    }
}
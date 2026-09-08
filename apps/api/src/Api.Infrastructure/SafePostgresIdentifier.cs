using System.Text.RegularExpressions;

namespace Api.Infrastructure;

internal static partial class SafePostgresIdentifier
{
    // Postgres's system-wide identifier length limit (NAMEDATALEN - 1); identifiers
    // longer than this are silently truncated rather than rejected.
    private const int MaxLength = 63;

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex Pattern();

    public static void EnsureSafe(string value, string paramName)
    {
        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"'{value}' exceeds the {MaxLength}-character Postgres identifier limit.", paramName);
        }

        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Postgres identifier.", paramName);
        }
    }
}

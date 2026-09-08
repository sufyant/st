using System.Text.RegularExpressions;

namespace Api.Infrastructure;

internal static partial class SafePostgresIdentifier
{
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex Pattern();

    public static void EnsureSafe(string value, string paramName)
    {
        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Postgres identifier.", paramName);
        }
    }
}

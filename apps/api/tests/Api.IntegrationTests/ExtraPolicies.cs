namespace Api.IntegrationTests;

// R11: every row level security policy besides the tenant isolation policy of each tenant table, with the one command it allows.
// DatabaseTables fails on any other policy, and on a listed one the database does not have.
internal static class ExtraPolicies
{
    public static readonly IReadOnlyList<(string Table, string Name, string Command)> Policies =
    [
        ("catalog.memberships", "own_memberships", "SELECT"),
    ];
}

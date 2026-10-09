namespace Api.IntegrationTests;

// R9: every tenant table of the database, with the SQL that writes one row of it for a tenant. The rows a table refers to are
// written in the same statement, so a failed write leaves nothing behind. A new tenant table is one more line here;
// TenantTableIsolationTests fails until it has one.
internal static class TenantTables
{
    private static readonly Dictionary<string, TenantTable> All = new(StringComparer.Ordinal)
    {
        ["audit.entries"] = new(
            ApplicationMayUpdate: false,
            tenant => $"INSERT INTO audit.entries (id, occurred_at, actor_id, kind, operation, tenant_id) VALUES (gen_random_uuid(), now(), 'user_isolation', 'Command', 'Isolation', '{tenant}')"),
        ["catalog.invitations"] = new(
            ApplicationMayUpdate: true,
            tenant => $"""
                WITH inviter AS (INSERT INTO catalog.users (id, external_id) VALUES (gen_random_uuid(), 'user_' || gen_random_uuid()) RETURNING id)
                INSERT INTO catalog.invitations (id, tenant_id, email, role_id, invited_by, created_at, expires_at, status)
                SELECT gen_random_uuid(), '{tenant}', 'invited@example.com', roles.id, inviter.id, now(), now() + interval '7 days', 'Pending'
                FROM inviter, catalog.roles WHERE roles.built_in = 'Member'
                """),
        ["catalog.memberships"] = new(
            ApplicationMayUpdate: true,
            tenant => $"""
                WITH member AS (INSERT INTO catalog.users (id, external_id) VALUES (gen_random_uuid(), 'user_' || gen_random_uuid()) RETURNING id)
                INSERT INTO catalog.memberships (tenant_id, user_id, role_id)
                SELECT '{tenant}', member.id, roles.id FROM member, catalog.roles WHERE roles.built_in = 'Member'
                """),
        ["probes.probes"] = new(
            ApplicationMayUpdate: true,
            tenant => $"INSERT INTO probes.probes (id, value, tenant_id) VALUES (gen_random_uuid(), 'isolation', '{tenant}')"),
    };

    public static IEnumerable<string> Names => All.Keys;

    // Audit entries are never changed once written, so the application role has no update privilege on them at all.
    public static IEnumerable<string> Updatable => All.Where(table => table.Value.ApplicationMayUpdate).Select(table => table.Key);

    public static string InsertRow(string table, Guid tenantId) => All[table].InsertRow(tenantId);

    private sealed record TenantTable(bool ApplicationMayUpdate, Func<Guid, string> InsertRow);
}

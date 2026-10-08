namespace Api.IntegrationTests;

// Writes catalog rows with plain SQL: the host's tests may not see ControlPlane's internals. Built-in roles are found by
// name, the way the catalog stores them.
internal sealed class Catalog(Database database)
{
    public async Task<(Guid Id, string Slug)> AddTenantAsync(string status = "Active", string? slug = null)
    {
        var id = Guid.NewGuid();
        slug ??= $"tenant-{id:N}"[..20];
        await database.ScalarAsync<object>($"INSERT INTO catalog.tenants (id, slug, status) VALUES ('{id}', '{slug}', '{status}')");

        return (id, slug);
    }

    public async Task<string> AddMemberAsync(Guid tenantId, string? externalId = null, string role = "Member")
    {
        externalId ??= await AddUserAsync();
        await database.ScalarAsync<object>(
            $"""
            INSERT INTO catalog.memberships (tenant_id, user_id, role_id)
            SELECT '{tenantId}', users.id, roles.id FROM catalog.users, catalog.roles
            WHERE users.external_id = '{externalId}' AND roles.built_in = '{role}'
            """);

        return externalId;
    }

    public async Task<string> AddUserAsync()
    {
        var externalId = $"user_{Guid.NewGuid():N}";
        await database.ScalarAsync<object>($"INSERT INTO catalog.users (id, external_id) VALUES ('{Guid.NewGuid()}', '{externalId}')");

        return externalId;
    }

    public async Task<string> AddSystemAdminAsync()
    {
        var externalId = await AddUserAsync();
        await database.ScalarAsync<object>(
            $"INSERT INTO catalog.system_admins (user_id, granted_at) SELECT id, now() FROM catalog.users WHERE external_id = '{externalId}'");

        return externalId;
    }

    public Task<long> CountAsync(string sql) => database.ScalarAsync<long>(sql);
}

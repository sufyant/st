namespace Api.IntegrationTests;

// Writes catalog rows with plain SQL: the host's tests may not see ControlPlane's internals. Built-in roles are found by
// name, the way the catalog stores them. A membership belongs to a tenant, so it is written with its tenant declared.
internal sealed class Catalog(Database database)
{
    public async Task<(Guid Id, string Slug)> AddTenantAsync(string status = "Active", string? slug = null, string name = "Acme Ltd", Guid? tenantId = null)
    {
        var id = tenantId ?? Guid.NewGuid();
        slug ??= $"tenant-{id:N}"[..20];
        await database.ScalarAsync<object>($"INSERT INTO catalog.tenants (id, name, slug, status) VALUES ('{id}', '{name}', '{slug}', '{status}')");

        return (id, slug);
    }

    public async Task<string> AddMemberAsync(Guid tenantId, string? externalId = null, string role = "Member")
    {
        externalId ??= await AddUserAsync();
        await database.ExecuteInTenantAsync(
            tenantId,
            $"""
            INSERT INTO catalog.memberships (tenant_id, user_id, role_id)
            SELECT '{tenantId}', users.id, roles.id FROM catalog.users, catalog.roles
            WHERE users.external_id = '{externalId}' AND roles.built_in = '{role}'
            """);

        return externalId;
    }

    public async Task<string> AddUserAsync(Guid? id = null)
    {
        var externalId = $"user_{Guid.NewGuid():N}";
        await database.ScalarAsync<object>($"INSERT INTO catalog.users (id, external_id) VALUES ('{id ?? Guid.NewGuid()}', '{externalId}')");

        return externalId;
    }

    // The catalog's own id of a user, which the API answers with; the identity provider's id is what a token carries.
    public Task<Guid> UserIdOfAsync(string externalId) =>
        database.ScalarAsync<Guid>($"SELECT id FROM catalog.users WHERE external_id = '{externalId}'");

    public async Task<string> AddSystemAdminAsync()
    {
        var externalId = await AddUserAsync();
        await database.ScalarAsync<object>(
            $"INSERT INTO catalog.system_admins (user_id, granted_at) SELECT id, now() FROM catalog.users WHERE external_id = '{externalId}'");

        return externalId;
    }

    public Task<long> CountAsync(string sql) => database.ScalarAsync<long>(sql);
}

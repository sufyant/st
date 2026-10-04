namespace Api.IntegrationTests;

// Writes catalog rows with plain SQL: the host's tests may not see ControlPlane's internals (0007).
internal sealed class Catalog(Database database)
{
    public async Task<(Guid Id, string Slug)> AddTenantAsync(string status = "Active", string? slug = null)
    {
        var id = Guid.NewGuid();
        slug ??= $"tenant-{id:N}"[..20];
        await database.ScalarAsync<object>($"INSERT INTO catalog.tenants (id, slug, status) VALUES ('{id}', '{slug}', '{status}')");

        return (id, slug);
    }

    public async Task<string> AddMemberAsync(Guid tenantId, string? externalId = null)
    {
        externalId ??= await AddUserAsync();
        await database.ScalarAsync<object>(
            $"INSERT INTO catalog.memberships (tenant_id, user_id) SELECT '{tenantId}', id FROM catalog.users WHERE external_id = '{externalId}'");

        return externalId;
    }

    public async Task<string> AddUserAsync()
    {
        var externalId = $"user_{Guid.NewGuid():N}";
        await database.ScalarAsync<object>($"INSERT INTO catalog.users (id, external_id) VALUES ('{Guid.NewGuid()}', '{externalId}')");

        return externalId;
    }
}

using Dapper;
using Domain.ControlPlane.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tenants;

public sealed record ResolvedTenant(Guid Id, string Alias, string DatabaseName, TenantStatus Status);

public sealed class TenantResolver(
    [FromKeyedServices("control-plane-read")] NpgsqlDataSource dataSource,
    IMemoryCache cache)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    public async Task<ResolvedTenant?> ResolveAsync(string alias, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(alias);

        if (cache.TryGetValue(cacheKey, out ResolvedTenant? cached))
        {
            return cached;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TenantRow>(new CommandDefinition(
            "SELECT id, alias, database_name AS databasename, status FROM control.tenants WHERE alias = @alias",
            new { alias },
            cancellationToken: cancellationToken));
        var resolved = row is null
            ? null
            : new ResolvedTenant(row.Id, row.Alias, row.DatabaseName, Enum.Parse<TenantStatus>(row.Status));

        cache.Set(cacheKey, resolved, CacheLifetime);

        return resolved;
    }

    public async Task<bool> HasMembershipAsync(
        Guid tenantId,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM control.memberships " +
            "WHERE tenant_id = @tenantId AND external_user_id = @externalUserId)",
            new { tenantId, externalUserId },
            cancellationToken: cancellationToken));
    }

    public void Evict(string alias) => cache.Remove(CacheKey(alias));

    private static string CacheKey(string alias) => $"tenant:{alias}";

    private sealed record TenantRow(Guid Id, string Alias, string DatabaseName, string Status);
}

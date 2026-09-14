using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Domain.ControlPlane.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tenants;

public sealed record ResolvedTenantCredential(string RoleName, string Password);

public sealed class TenantCredentialResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly NpgsqlDataSource dataSource;
    private readonly IMemoryCache cache;
    private readonly IDataProtector protector;

    public TenantCredentialResolver(
        [FromKeyedServices("control-plane-read")] NpgsqlDataSource dataSource,
        IDataProtectionProvider dataProtectionProvider,
        IMemoryCache cache)
    {
        this.dataSource = dataSource;
        this.cache = cache;
        protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
    }

    public async Task<ResolvedTenantCredential?> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(tenantId);

        if (cache.TryGetValue(cacheKey, out ResolvedTenantCredential? cached))
        {
            return cached;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CredentialRow>(new CommandDefinition(
            "SELECT role_name AS rolename, encrypted_password AS encryptedpassword " +
            "FROM control.tenant_credentials WHERE tenant_id = @tenantId",
            new { tenantId },
            cancellationToken: cancellationToken));
        var resolved = row is null
            ? null
            : new ResolvedTenantCredential(row.RoleName, protector.Unprotect(row.EncryptedPassword));

        cache.Set(cacheKey, resolved, CacheLifetime);

        return resolved;
    }

    public void Evict(Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private static string CacheKey(Guid tenantId) => $"tenant-credential:{tenantId:N}";

    private sealed record CredentialRow(string RoleName, string EncryptedPassword);
}

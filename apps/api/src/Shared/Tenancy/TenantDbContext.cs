using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Tenancy;

/// <summary>
/// Base for a module DbContext that holds tenant entities. Every <see cref="ITenantEntity"/> gets a tenant column that defaults
/// to the declared tenant; the migrations add the matching row level security, enabled and forced. Row level security is the
/// only filter: there is no query filter on the tenant.
/// </summary>
public abstract class TenantDbContext(DbContextOptions options) : DbContext(options)
{
    private const string UserSetting = "app.user_id";

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        BuildModel(modelBuilder);

        var tenantEntities = modelBuilder.Model.GetEntityTypes()
            .Where(entity => entity.BaseType is null && typeof(ITenantEntity).IsAssignableFrom(entity.ClrType))
            .ToList();

        foreach (var entity in tenantEntities)
        {
            var builder = modelBuilder.Entity(entity.ClrType);
            builder.Property<Guid>(TenantColumn.Property).HasDefaultValueSql(TenantColumn.CurrentTenantSql);

            // A key that leads with the tenant column indexes it already.
            if (entity.FindPrimaryKey()?.Properties[0].Name != TenantColumn.Property)
            {
                builder.HasIndex(TenantColumn.Property);
            }
        }
    }

    /// <summary>
    /// Declares the tenant for the rest of the current transaction (R4). Outside a transaction the setting would end with the
    /// statement, so it is refused.
    /// </summary>
    public async Task DeclareTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A tenant is declared inside a transaction, never on the connection.");
        }

        await Database.ExecuteSqlAsync($"SELECT set_config({TenantColumn.Setting}, {tenantId.ToString()}, true)", cancellationToken);
    }

    /// <summary>
    /// Declares the catalog user for the rest of the current transaction, the way the tenant is declared, for the one policy that
    /// reads by user rather than by tenant (R11). Outside a transaction it is refused.
    /// </summary>
    public async Task DeclareUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A user is declared inside a transaction, never on the connection.");
        }

        await Database.ExecuteSqlAsync($"SELECT set_config({UserSetting}, {userId.ToString()}, true)", cancellationToken);
    }

    protected abstract void BuildModel(ModelBuilder modelBuilder);
}

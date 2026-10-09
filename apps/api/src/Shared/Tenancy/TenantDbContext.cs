using System.Data.Common;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;
using Wolverine;

namespace Tenancy;

/// <summary>
/// Base for a module DbContext that holds tenant entities. Every <see cref="ITenantEntity"/> gets a tenant column that defaults
/// to the declared tenant; the migrations add the matching row level security, enabled and forced. Row level security is the
/// only filter: there is no query filter on the tenant.
/// </summary>
/// <remarks>
/// The tenant is the tenant of the message the DbContext serves (W2): Wolverine passes the handler's own message context to the
/// DbContext it creates for the handler, and each transaction the DbContext begins declares that tenant.
/// </remarks>
public abstract class TenantDbContext(DbContextOptions options, IMessageContext? messaging) : DbContext(options)
{
    /// <summary>
    /// The tenant of the message this DbContext serves. There is none outside a handler, for a message sent without a tenant, and
    /// for Wolverine's default tenant id, which a stored message sent without a tenant comes back with (W3).
    /// </summary>
    public Guid? MessageTenantId =>
        messaging?.TenantId is { Length: > 0 } tenantId && tenantId != StorageConstants.DefaultTenantId ? Guid.Parse(tenantId) : null;

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
    /// Declares the tenant for the rest of the current transaction (R4), for a read outside a handler. Outside a transaction the
    /// setting would end with the statement, so it is refused; so is a transaction that has declared a user.
    /// </summary>
    public Task DeclareTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Declaration.DeclareTenantAsync(
            Database.GetDbConnection(),
            CurrentTransaction("A tenant is declared inside a transaction, never on the connection."),
            tenantId,
            cancellationToken);

    /// <summary>
    /// Declares the catalog user for the rest of the current transaction, the way the tenant is declared, for the one policy that
    /// reads by user rather than by tenant (R11). Outside a transaction it is refused; so is a transaction that has declared a
    /// tenant.
    /// </summary>
    public Task DeclareUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Declaration.DeclareUserAsync(
            Database.GetDbConnection(),
            CurrentTransaction("A user is declared inside a transaction, never on the connection."),
            userId,
            cancellationToken);

    protected abstract void BuildModel(ModelBuilder modelBuilder);

    private DbTransaction CurrentTransaction(string refusal) =>
        Database.CurrentTransaction?.GetDbTransaction() ?? throw new InvalidOperationException(refusal);
}

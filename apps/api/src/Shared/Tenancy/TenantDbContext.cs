using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Tenancy;

/// <summary>
/// Base for a module DbContext that holds tenant entities. Every <see cref="ITenantEntity"/> gets a shadow tenant column that
/// defaults to the active tenant setting and a query filter on the active tenant; the migrations add the matching row level
/// security policy (0014).
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, TenantContext tenant) : DbContext(options)
{
    private const string QueryFilter = "Tenant";

    // EF Core reads this from the context running the query, not from the one the model was built with.
    private Guid? CurrentTenantId => tenant.TenantId;

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
            builder.HasIndex(TenantColumn.Property);
            builder.HasQueryFilter(QueryFilter, BelongsToCurrentTenant(entity.ClrType));
        }
    }

    protected abstract void BuildModel(ModelBuilder modelBuilder);

    private LambdaExpression BelongsToCurrentTenant(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "entity");
        var tenantColumn = Expression.Call(
            typeof(EF), nameof(EF.Property), [typeof(Guid?)], entity, Expression.Constant(TenantColumn.Property));
        var currentTenant = Expression.Property(Expression.Constant(this, typeof(TenantDbContext)), nameof(CurrentTenantId));

        return Expression.Lambda(Expression.Equal(tenantColumn, currentTenant), entity);
    }
}

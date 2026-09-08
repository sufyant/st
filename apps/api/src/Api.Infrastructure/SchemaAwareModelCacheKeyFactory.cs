using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Api.Infrastructure;

public sealed class SchemaAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TenantDbContext tenantDbContext
            ? (tenantDbContext.SchemaName, designTime)
            : (object)designTime;
}

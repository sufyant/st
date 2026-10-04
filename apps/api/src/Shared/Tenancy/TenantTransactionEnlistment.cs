using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenancy;

// EF Core would begin a transaction of its own when saving; inside a tenant transaction the save joins it instead, so the work
// commits or rolls back with the request or message (0016).
internal sealed class TenantTransactionEnlistment(TenantTransaction transaction) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (transaction.Current is { } current && eventData.Context is { } context && context.Database.CurrentTransaction is null)
        {
            await context.Database.UseTransactionAsync(current, cancellationToken);
        }

        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        throw new InvalidOperationException("Save changes asynchronously; the tenant transaction is joined asynchronously.");
}

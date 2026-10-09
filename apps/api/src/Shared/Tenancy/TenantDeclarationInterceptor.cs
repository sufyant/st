using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenancy;

// Declares the tenant of the message a module DbContext serves, as the first statement of each transaction the DbContext begins,
// on the same connection and in the same transaction (W2, R4). In a handler that is the transaction Wolverine begins. The setting
// ends with the transaction, so a pooled connection never carries it on. A DbContext without a message tenant declares nothing,
// and row level security then shows it no tenant rows (R5).
internal sealed class TenantDeclarationInterceptor : DbTransactionInterceptor
{
    public static TenantDeclarationInterceptor Instance { get; } = new();

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is TenantDbContext { MessageTenantId: { } tenantId })
        {
            await Declaration.DeclareTenantAsync(connection, result, tenantId, cancellationToken);
        }

        return result;
    }

    // Wolverine begins its transactions asynchronously, and so does every other caller here; a synchronous transaction of a tenant
    // would run without the tenant declared.
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) =>
        eventData.Context is TenantDbContext { MessageTenantId: not null }
            ? throw new NotSupportedException("A transaction of a tenant is begun asynchronously, so that its tenant is declared.")
            : result;
}

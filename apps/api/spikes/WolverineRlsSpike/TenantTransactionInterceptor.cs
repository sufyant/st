using System.Data.Common;
using JasperFx;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WolverineRlsSpike;

// Mechanism A: declare the tenant as the first statement of the transaction Wolverine begins.
public sealed class TenantTransactionInterceptor : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        // A message sent without a tenant carries Wolverine's "*DEFAULT*" sentinel, not null. Declare nothing then.
        if (eventData.Context is ITenantAware { CurrentTenantId: { Length: > 0 } tenantId }
            && tenantId != StorageConstants.DefaultTenantId)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = result;
            cmd.CommandText = "SELECT set_config('app.tenant_id', @tenant, true)";
            var p = cmd.CreateParameter();
            p.ParameterName = "tenant";
            p.Value = tenantId;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result) =>
        throw new NotSupportedException("Wolverine begins the transaction asynchronously.");
}

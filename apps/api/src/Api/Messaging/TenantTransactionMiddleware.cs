using JasperFx;
using SharedKernel;
using Tenancy;
using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Api.Messaging;

// Runs every message that carries a tenant in one transaction with the tenant set at its start, whether it comes from a request,
// a cascade or a queue (0016, 0017). Messages without a tenant, such as catalog work, run without one. The transaction commits
// when the handler succeeds; an exception, or a Result that reports a failure, leaves it uncommitted, and disposing the
// message's scope rolls it back. TenantTransactionPolicy places these calls; Wolverine's generated code makes them, so the class
// is public.
public static class TenantTransactionMiddleware
{
    public static async Task BeginAsync(
        Envelope envelope,
        TenantTransaction transaction,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        // A stored message sent without a tenant comes back with Wolverine's default tenant id, which names no tenant.
        if (string.IsNullOrEmpty(envelope.TenantId) || envelope.TenantId == StorageConstants.DefaultTenantId)
        {
            return;
        }

        await transaction.BeginAsync(Guid.Parse(envelope.TenantId), cancellationToken);

        // The messages the handler sends are stored in the same transaction, so they are kept or lost with its work (0024).
        await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction((IMessageDatabase)context.Storage, transaction.Current!));
    }

    public static Task CommitAsync(TenantTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Current is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    // An expected failure undoes the handler's work just as an exception does (0032), and the messages it sent are dropped with it,
    // whether or not the message carries a tenant.
    public static Task CommitIfSucceededAsync(
        Result result,
        TenantTransaction transaction,
        MessageContext context,
        CancellationToken cancellationToken) =>
        result.IsSuccess ? CommitAsync(transaction, cancellationToken) : context.ClearAllAsync().AsTask();
}

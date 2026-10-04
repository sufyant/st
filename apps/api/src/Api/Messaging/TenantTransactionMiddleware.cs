using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.Messaging;

// Runs every message that carries a tenant in one transaction with the tenant set at its start, whether it comes from a request,
// a cascade or a queue (0016, 0017). Messages without a tenant, such as catalog work, run without one. The transaction commits
// when the handler succeeds; an exception, or a Result that reports a failure, leaves it uncommitted, and disposing the
// message's scope rolls it back. TenantTransactionPolicy places these calls; Wolverine's generated code makes them, so the
// class is public.
public static class TenantTransactionMiddleware
{
    public static Task BeginAsync(Envelope envelope, TenantTransaction transaction, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(envelope.TenantId)
            ? Task.CompletedTask
            : transaction.BeginAsync(Guid.Parse(envelope.TenantId), cancellationToken);

    public static Task CommitAsync(TenantTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Current is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    // An expected failure undoes the handler's work just as an exception does (0032).
    public static Task CommitIfSucceededAsync(Result result, TenantTransaction transaction, CancellationToken cancellationToken) =>
        result.IsSuccess ? CommitAsync(transaction, cancellationToken) : Task.CompletedTask;
}

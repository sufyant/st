using ControlPlane.Application.Members;
using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// A tenant always keeps at least one owner (0030), also when owners change roles at the same moment.
public sealed class LastOwnerTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_last_owner_cannot_be_demoted()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var owner = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);

        var changed = await Handlers.ChangeMemberRoleAsync(database.Services, tenant.Id, owner.ExternalId, owner.Id, BuiltInRoles.Admin.Id);

        changed.Error.Code.ShouldBe("membership.last_owner");
    }

    [Fact]
    public async Task Two_owners_demoting_each_other_at_once_leave_one_owner()
    {
        var tenant = await Catalog.AddTenantAsync(database.Services);
        var first = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        var second = await Catalog.AddMemberAsync(database.Services, tenant, BuiltInRoles.Owner);
        await using var firstScope = database.Services.CreateAsyncScope();
        await using var secondScope = database.Services.CreateAsyncScope();
        var firstTransaction = await BeginAsync(firstScope, tenant.Id);
        var secondTransaction = await BeginAsync(secondScope, tenant.Id);
        (await DemoteAsync(firstScope, first.ExternalId, second.Id)).IsSuccess.ShouldBeTrue();

        var secondDemotion = DemoteAsync(secondScope, second.ExternalId, first.Id);
        await WaitUntilBlockedAsync(secondTransaction.Connection.ProcessID);
        await firstTransaction.CommitAsync(Cancellation);

        (await secondDemotion).IsSuccess.ShouldBeFalse();
        (await CountOwnersAsync(tenant.Id)).ShouldBe(1);
    }

    private static async Task<TenantTransaction> BeginAsync(AsyncServiceScope scope, Guid tenantId)
    {
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, Cancellation);
        return transaction;
    }

    private static Task<SharedKernel.Result> DemoteAsync(AsyncServiceScope scope, string actorId, Guid userId) =>
        ChangeMemberRoleHandler.HandleAsync(
            new ChangeMemberRole(actorId, userId, BuiltInRoles.Admin.Id), scope.ServiceProvider.GetRequiredService<ITenantCatalog>(), Cancellation);

    // The second change must be waiting for the first one's lock before the first commits, or the test would prove nothing. If it
    // never waits, there is no lock, and the wait ends in a timeout that fails the test.
    private async Task WaitUntilBlockedAsync(int processId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(DatabaseRoles.Application));
        await connection.OpenAsync(timeout.Token);
        await using var blocked = new NpgsqlCommand($"SELECT cardinality(pg_blocking_pids({processId})) > 0", connection);
        while (!(bool)(await blocked.ExecuteScalarAsync(timeout.Token))!)
        {
            timeout.Token.ThrowIfCancellationRequested();
        }
    }

    private async Task<long> CountOwnersAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(DatabaseRoles.Application));
        await connection.OpenAsync(Cancellation);
        await using var count = new NpgsqlCommand(
            $"SELECT count(*) FROM catalog.memberships WHERE tenant_id = '{tenantId}' AND role_id = '{BuiltInRoles.Owner.Id}'", connection);

        return (long)(await count.ExecuteScalarAsync(Cancellation))!;
    }
}

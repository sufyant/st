using Audit.Application;
using Audit.Application.Ports;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Audit.IntegrationTests;

// The Audit module records that a tenant was created when ControlPlane announces its activation, under that tenant.
public sealed class TenantCreatedAuditTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordTenantCreated_TenantActivated_WritesOneEntryUnderTheTenantByItsCreator()
    {
        var activated = Activated();

        await InTenant.RunAsync(database.Services, activated.TenantId, scope => RecordAsync(scope, activated));

        (await ReadAsync<string>(activated.TenantId, $"SELECT operation || '|' || actor_id || '|' || kind FROM audit.entries WHERE id = '{activated.EventId}'"))
            .ShouldBe($"tenant.created|{activated.CreatedBy}|Command");
        (await ReadAsync<bool>(activated.TenantId, $"SELECT occurred_at = '2026-10-09T09:00:00Z' FROM audit.entries WHERE id = '{activated.EventId}'"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task RecordTenantCreated_InAnotherTenant_IsNotSeen()
    {
        var activated = Activated();

        await InTenant.RunAsync(database.Services, activated.TenantId, scope => RecordAsync(scope, activated));

        (await CountAsync(Guid.NewGuid(), activated.EventId)).ShouldBe(0);
    }

    // An event may arrive twice; its id, chosen by its publisher, is the entry's id, so it is written once.
    [Fact]
    public async Task RecordTenantCreated_EventArrivesTwice_WritesOneEntry()
    {
        var activated = Activated();

        await InTenant.RunAsync(database.Services, activated.TenantId, scope => RecordAsync(scope, activated));
        await InTenant.RunAsync(database.Services, activated.TenantId, scope => RecordAsync(scope, activated));

        (await CountAsync(activated.TenantId, activated.EventId)).ShouldBe(1);
    }

    // Audit records are never changed once written: the application role may add and read them, nothing more.
    [Theory]
    [InlineData("UPDATE audit.entries SET operation = 'rewritten'")]
    [InlineData("DELETE FROM audit.entries")]
    public async Task ChangeAuditEntry_AsTheApplication_IsRefused(string sql)
    {
        var activated = Activated();
        await InTenant.RunAsync(database.Services, activated.TenantId, scope => RecordAsync(scope, activated));

        var change = () => InTenant.SqlAsync(database.Services, activated.TenantId, command => command.ExecuteNonQueryAsync(Cancellation), sql);

        (await change.ShouldThrowAsync<PostgresException>()).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static TenantActivated Activated() => new(Guid.CreateVersion7(), OccurredAt, Guid.CreateVersion7(), "Acme Ltd", Guid.CreateVersion7());

    private static Task RecordAsync(IServiceProvider scope, TenantActivated activated) =>
        RecordTenantCreatedHandler.HandleAsync(activated, scope.GetRequiredService<IAuditLog>(), Cancellation);

    private Task<long> CountAsync(Guid tenant, Guid entryId) => ReadAsync<long>(tenant, $"SELECT count(*) FROM audit.entries WHERE id = '{entryId}'");

    private Task<T> ReadAsync<T>(Guid tenant, string sql) =>
        InTenant.SqlAsync(database.Services, tenant, async command => (T)(await command.ExecuteScalarAsync(Cancellation))!, sql);
}

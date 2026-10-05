using Audit.Application;
using Audit.Application.Ports;
using Audit.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Audit.IntegrationTests;

// The Audit module stores the records other modules and the pipeline send it, under the tenant they were sent in (0040).
public sealed class RecordAuditEntryTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_entry_is_stored_under_its_tenant_and_only_that_tenant_sees_it()
    {
        var (tenant, other) = (Guid.NewGuid(), Guid.NewGuid());
        var entry = Entry();

        await InTenant.RunAsync(database.Services, tenant, scope => RecordAsync(scope, entry));

        (await CountAsync(tenant, entry.EntryId)).ShouldBe(1);
        (await CountAsync(other, entry.EntryId)).ShouldBe(0);
    }

    // A message may arrive twice (0024); the entry's id, chosen by its sender, keeps it from being stored twice.
    [Fact]
    public async Task An_entry_that_arrives_twice_is_stored_once()
    {
        var tenant = Guid.NewGuid();
        var entry = Entry();

        await InTenant.RunAsync(database.Services, tenant, scope => RecordAsync(scope, entry));
        await InTenant.RunAsync(database.Services, tenant, scope => RecordAsync(scope, entry));

        (await CountAsync(tenant, entry.EntryId)).ShouldBe(1);
    }

    // Audit records are never changed once written: the application role may add and read them, nothing more.
    [Theory]
    [InlineData("UPDATE audit.entries SET operation = 'rewritten'")]
    [InlineData("DELETE FROM audit.entries")]
    public async Task The_application_cannot_change_or_remove_entries(string sql)
    {
        var tenant = Guid.NewGuid();
        await InTenant.RunAsync(database.Services, tenant, scope => RecordAsync(scope, Entry()));

        var change = () => InTenant.SqlAsync(database.Services, tenant, command => command.ExecuteNonQueryAsync(Cancellation), sql);

        (await change.ShouldThrowAsync<PostgresException>()).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static RecordAuditEntry Entry() =>
        new(Guid.CreateVersion7(), new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero), "user_ada", AuditKind.Command, "CreateRole", """{"command":{"name":"Recruiter"}}""");

    private static Task RecordAsync(IServiceProvider scope, RecordAuditEntry entry) =>
        RecordAuditEntryHandler.HandleAsync(entry, scope.GetRequiredService<IAuditLog>(), Cancellation);

    private Task<long> CountAsync(Guid tenant, Guid entryId) =>
        InTenant.SqlAsync(
            database.Services,
            tenant,
            async command => (long)(await command.ExecuteScalarAsync(Cancellation))!,
            $"SELECT count(*) FROM audit.entries WHERE id = '{entryId}'");
}

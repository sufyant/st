using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tenancy.IntegrationTests;

public sealed class TenantIsolationTests(Database database) : IAsyncDisposable
{
    private readonly Notes _notes = new(database.ApplicationConnectionString);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _notes.DisposeAsync();

    [Fact]
    public async Task A_tenant_sees_its_own_rows()
    {
        var tenant = Tenants.New();
        await WriteNoteAsync(tenant, "own note");

        var texts = await _notes.InTenantAsync(tenant, notes => notes.Notes.Select(note => note.Text).ToListAsync(Cancellation));

        texts.ShouldBe(["own note"]);
    }

    [Fact]
    public async Task A_new_row_belongs_to_the_active_tenant()
    {
        var tenant = Tenants.New();

        var note = await WriteNoteAsync(tenant, "stamped");

        var owner = await _notes.InTenantAsync(tenant, notes =>
            notes.Notes.Where(row => row.Id == note.Id).Select(row => EF.Property<Guid>(row, "TenantId")).SingleAsync(Cancellation));
        owner.ShouldBe(tenant);
    }

    [Fact]
    public async Task Rows_of_another_tenant_are_not_returned()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");

        var count = await _notes.InTenantAsync(Tenants.New(), notes => notes.Notes.CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Row_level_security_alone_hides_rows_of_another_tenant()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");

        var count = await _notes.InTenantAsync(Tenants.New(), notes => notes.Notes.IgnoreQueryFilters().CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    // The owner owns the table and is not subject to row level security, so only the query filter stands between it and the row.
    [Fact]
    public async Task The_query_filter_alone_hides_rows_of_another_tenant()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");
        await using var asOwner = new Notes(database.OwnerConnectionString);

        var count = await asOwner.InTenantAsync(Tenants.New(), notes => notes.Notes.CountAsync(note => note.Text == "secret", Cancellation));
        var withoutFilter = await asOwner.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.IgnoreQueryFilters().CountAsync(note => EF.Property<Guid>(note, "TenantId") == other, Cancellation));

        count.ShouldBe(0);
        withoutFilter.ShouldBe(1);
    }

    [Fact]
    public async Task Rows_of_another_tenant_cannot_be_updated()
    {
        var other = Tenants.New();
        var note = await WriteNoteAsync(other, "original");

        var updated = await _notes.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.IgnoreQueryFilters().Where(row => row.Id == note.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Text, "changed"), Cancellation));

        updated.ShouldBe(0);
        (await ReadTextAsync(other, note.Id)).ShouldBe("original");
    }

    [Fact]
    public async Task Rows_of_another_tenant_cannot_be_deleted()
    {
        var other = Tenants.New();
        var note = await WriteNoteAsync(other, "kept");

        var deleted = await _notes.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.IgnoreQueryFilters().Where(row => row.Id == note.Id).ExecuteDeleteAsync(Cancellation));

        deleted.ShouldBe(0);
        (await ReadTextAsync(other, note.Id)).ShouldBe("kept");
    }

    [Fact]
    public async Task A_row_cannot_be_written_for_another_tenant()
    {
        var other = Tenants.New();

        var write = () => _notes.InTenantAsync(Tenants.New(), async notes =>
        {
            var note = new Note { Text = "planted" };
            notes.Notes.Add(note);
            notes.Entry(note).Property("TenantId").CurrentValue = other;
            await notes.SaveChangesAsync(Cancellation);
        });

        var failure = await write.ShouldThrowAsync<DbUpdateException>();
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Without_a_tenant_no_rows_are_returned()
    {
        await WriteNoteAsync(Tenants.New(), "secret");

        var count = await _notes.WithoutTenantAsync(notes => notes.Notes.IgnoreQueryFilters().CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_tenant_no_row_can_be_written()
    {
        var write = () => _notes.WithoutTenantAsync(async notes =>
        {
            notes.Notes.Add(new Note { Text = "orphan" });
            return await notes.SaveChangesAsync(Cancellation);
        });

        var failure = await write.ShouldThrowAsync<DbUpdateException>();
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Work_that_is_not_committed_is_discarded()
    {
        var tenant = Tenants.New();
        var note = new Note { Text = "abandoned" };

        await using (var scope = _notes.CreateScope())
        {
            await Notes.BeginUncommittedAsync(scope, tenant);
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            notes.Notes.Add(note);
            await notes.SaveChangesAsync(Cancellation);
        }

        (await _notes.InTenantAsync(tenant, notes => notes.Notes.AnyAsync(row => row.Id == note.Id, Cancellation))).ShouldBeFalse();
    }

    // A save of several rows would begin a transaction of its own; it has to join the tenant transaction instead.
    [Fact]
    public async Task Several_rows_saved_together_are_discarded_with_their_transaction()
    {
        var tenant = Tenants.New();
        Note[] written = [new() { Text = "first" }, new() { Text = "second" }];

        await using (var scope = _notes.CreateScope())
        {
            await Notes.BeginUncommittedAsync(scope, tenant);
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            notes.Notes.AddRange(written);
            await notes.SaveChangesAsync(Cancellation);
        }

        (await _notes.InTenantAsync(tenant, notes => notes.Notes.CountAsync(Cancellation))).ShouldBe(0);
    }

    // Requests share pooled connections; the tenant set for one transaction must be gone when the connection is used again.
    [Fact]
    public async Task The_tenant_setting_ends_with_its_transaction()
    {
        await using var scope = _notes.CreateScope();
        var transaction = await Notes.BeginUncommittedAsync(scope, Tenants.New());
        await transaction.CommitAsync(Cancellation);

        await using var command = new NpgsqlCommand("SELECT NULLIF(current_setting('app.tenant_id', true), '')", transaction.Connection);
        var setting = await command.ExecuteScalarAsync(Cancellation);

        setting.ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task No_database_role_bypasses_row_level_security()
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_roles WHERE rolname IN ('api_owner', 'api_application') AND NOT rolbypassrls AND NOT rolsuper",
            connection);

        var rolesWithoutBypass = (long)(await command.ExecuteScalarAsync(Cancellation))!;

        rolesWithoutBypass.ShouldBe(2);
    }

    private async Task<Note> WriteNoteAsync(Guid tenant, string text)
    {
        var note = new Note { Text = text };
        await _notes.InTenantAsync(tenant, async notes =>
        {
            notes.Notes.Add(note);
            await notes.SaveChangesAsync(Cancellation);
        });

        return note;
    }

    private Task<string> ReadTextAsync(Guid tenant, Guid id) =>
        _notes.InTenantAsync(tenant, notes => notes.Notes.Where(note => note.Id == id).Select(note => note.Text).SingleAsync(Cancellation));
}

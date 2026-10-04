using Npgsql;

namespace Tenancy.IntegrationTests;

public sealed class BackgroundScanTests(Database database) : IAsyncDisposable
{
    private readonly Notes _notes = new(database.ApplicationConnectionString);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _notes.DisposeAsync();

    [Fact]
    public async Task A_scan_finds_due_items_across_tenants_as_tenant_and_id_only()
    {
        var first = Tenants.New();
        var second = Tenants.New();
        var firstNote = await WriteNoteAsync(first, "scan-me-7f3a");
        var secondNote = await WriteNoteAsync(second, "scan-me-7f3a");

        var found = await ScanAsync("scan-me-7f3a");

        found.ShouldBe([(first, firstNote), (second, secondNote)], ignoreOrder: true);
    }

    [Fact]
    public async Task No_database_role_bypasses_row_level_security()
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_roles WHERE rolname IN ('api_owner', 'api_application', 'api_reporting') AND NOT rolbypassrls AND NOT rolsuper",
            connection);

        var rolesWithoutBypass = (long)(await command.ExecuteScalarAsync(Cancellation))!;

        rolesWithoutBypass.ShouldBe(3);
    }

    private async Task<Guid> WriteNoteAsync(Guid tenant, string text)
    {
        var note = new Note { Text = text };
        await _notes.InTenantAsync(tenant, async notes =>
        {
            notes.Notes.Add(note);
            await notes.SaveChangesAsync(Cancellation);
        });

        return note.Id;
    }

    private async Task<List<(Guid TenantId, Guid Id)>> ScanAsync(string fragment)
    {
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT tenant_id, id FROM fixture.notes_containing(@fragment)", connection);
        command.Parameters.AddWithValue("fragment", fragment);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        List<(Guid, Guid)> found = [];
        while (await reader.ReadAsync(Cancellation))
        {
            found.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }

        return found;
    }
}

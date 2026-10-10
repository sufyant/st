using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tenancy.IntegrationTests;

public sealed class TenantIsolationTests(Database database) : IAsyncDisposable
{
    private readonly Notes _notes = new(database.ApplicationConnectionString);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _notes.DisposeAsync();

    // R11 declares the user the way R4 declares the tenant: local to a transaction, never on the connection.
    [Fact]
    public async Task DeclareUser_OutsideATransaction_IsRefused()
    {
        var declare = () => _notes.WithoutTenantAsync(async notes =>
        {
            await notes.DeclareUserAsync(Guid.NewGuid(), Cancellation);
            return true;
        });

        await declare.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeclareUser_InATransaction_EndsWithTheTransaction()
    {
        var userId = Guid.NewGuid();

        var (inside, after) = await _notes.WithoutTenantAsync(async notes =>
        {
            await using (var transaction = await notes.Database.BeginTransactionAsync(Cancellation))
            {
                await notes.DeclareUserAsync(userId, Cancellation);
                var declared = await UserSettingAsync(notes);
                await transaction.CommitAsync(Cancellation);

                return (declared, await UserSettingAsync(notes));
            }
        });

        inside.ShouldBe(userId.ToString());
        after.ShouldBeNullOrEmpty();
    }

    // R4: the two policies combine with OR, so a transaction that declared both would read the user's memberships in other tenants.
    [Fact]
    public async Task DeclareUser_AfterATenant_IsRefused()
    {
        var declare = () => _notes.WithoutTenantAsync(async notes =>
        {
            await using var transaction = await notes.Database.BeginTransactionAsync(Cancellation);
            await notes.DeclareTenantAsync(Tenants.New(), Cancellation);
            await notes.DeclareUserAsync(Guid.NewGuid(), Cancellation);
            return true;
        });

        (await declare.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("a tenant or a user, never both");
    }

    [Fact]
    public async Task DeclareTenant_AfterAUser_IsRefused()
    {
        var declare = () => _notes.WithoutTenantAsync(async notes =>
        {
            await using var transaction = await notes.Database.BeginTransactionAsync(Cancellation);
            await notes.DeclareUserAsync(Guid.NewGuid(), Cancellation);
            await notes.DeclareTenantAsync(Tenants.New(), Cancellation);
            return true;
        });

        (await declare.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("a tenant or a user, never both");
    }

    // R4: a transaction declares one tenant only. Its writes so far belong to the first, so declaring another one stops it.
    [Fact]
    public async Task DeclareTenant_AnotherTenantInTheSameTransaction_IsRefusedAndWritesNothing()
    {
        var first = Tenants.New();
        var second = Tenants.New();

        var declare = () => _notes.WithoutTenantAsync(async notes =>
        {
            await using var transaction = await notes.Database.BeginTransactionAsync(Cancellation);
            await notes.DeclareTenantAsync(first, Cancellation);
            notes.Notes.Add(new Note { Text = "first" });
            await notes.SaveChangesAsync(Cancellation);
            await notes.DeclareTenantAsync(second, Cancellation);
            notes.Notes.Add(new Note { Text = "second" });
            await notes.SaveChangesAsync(Cancellation);
            await transaction.CommitAsync(Cancellation);
            return true;
        });

        (await declare.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("one tenant only");
        (await _notes.InTenantAsync(first, notes => notes.Notes.CountAsync(Cancellation))).ShouldBe(0);
        (await _notes.InTenantAsync(second, notes => notes.Notes.CountAsync(Cancellation))).ShouldBe(0);
    }

    [Fact]
    public async Task DeclareTenant_TheSameTenantAgain_IsAllowed()
    {
        var tenant = Tenants.New();

        var declared = await _notes.WithoutTenantAsync(async notes =>
        {
            await using var transaction = await notes.Database.BeginTransactionAsync(Cancellation);
            await notes.DeclareTenantAsync(tenant, Cancellation);
            await notes.DeclareTenantAsync(tenant, Cancellation);
            notes.Notes.Add(new Note { Text = "again" });
            await notes.SaveChangesAsync(Cancellation);
            await transaction.CommitAsync(Cancellation);
            return true;
        });

        declared.ShouldBeTrue();
        (await _notes.InTenantAsync(tenant, notes => notes.Notes.Select(note => note.Text).ToListAsync(Cancellation))).ShouldBe(["again"]);
    }

    [Fact]
    public async Task ReadNotes_InItsOwnTenant_ReturnsItsRows()
    {
        var tenant = Tenants.New();
        await WriteNoteAsync(tenant, "own note");

        var texts = await _notes.InTenantAsync(tenant, notes => notes.Notes.Select(note => note.Text).ToListAsync(Cancellation));

        texts.ShouldBe(["own note"]);
    }

    [Fact]
    public async Task WriteNote_InATenant_BelongsToThatTenant()
    {
        var tenant = Tenants.New();

        var note = await WriteNoteAsync(tenant, "stamped");

        var owner = await _notes.InTenantAsync(tenant, notes =>
            notes.Notes.Where(row => row.Id == note.Id).Select(row => EF.Property<Guid>(row, "TenantId")).SingleAsync(Cancellation));
        owner.ShouldBe(tenant);
    }

    [Fact]
    public async Task ReadNotes_OfAnotherTenant_ReturnsNoRows()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");

        var count = await _notes.InTenantAsync(Tenants.New(), notes => notes.Notes.CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    [Fact]
    public async Task ReadNotes_WithRowLevelSecurityAlone_HidesAnotherTenantsRows()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");

        var count = await _notes.InTenantAsync(Tenants.New(), notes => notes.Notes.CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    // R1: row level security is forced, so it binds the owner of the table too, which runs the migrations.
    [Fact]
    public async Task ReadNotes_AsTheOwner_SeesNoRowsOfAnotherTenantNorWithoutATenant()
    {
        var other = Tenants.New();
        await WriteNoteAsync(other, "secret");
        await using var asOwner = new Notes(database.OwnerConnectionString);

        var inAnotherTenant = await asOwner.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.CountAsync(note => EF.Property<Guid>(note, "TenantId") == other, Cancellation));
        var withoutATenant = await asOwner.WithoutTenantAsync(notes =>
            notes.Notes.CountAsync(note => EF.Property<Guid>(note, "TenantId") == other, Cancellation));

        inAnotherTenant.ShouldBe(0);
        withoutATenant.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateNotes_OfAnotherTenant_ChangesNoRow()
    {
        var other = Tenants.New();
        var note = await WriteNoteAsync(other, "original");

        var updated = await _notes.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.Where(row => row.Id == note.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Text, "changed"), Cancellation));

        updated.ShouldBe(0);
        (await ReadTextAsync(other, note.Id)).ShouldBe("original");
    }

    [Fact]
    public async Task DeleteNotes_OfAnotherTenant_RemovesNoRow()
    {
        var other = Tenants.New();
        var note = await WriteNoteAsync(other, "kept");

        var deleted = await _notes.InTenantAsync(Tenants.New(), notes =>
            notes.Notes.Where(row => row.Id == note.Id).ExecuteDeleteAsync(Cancellation));

        deleted.ShouldBe(0);
        (await ReadTextAsync(other, note.Id)).ShouldBe("kept");
    }

    [Fact]
    public async Task WriteNote_ForAnotherTenant_IsRefused()
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
    public async Task ReadNotes_WithoutATenant_ReturnsNoRows()
    {
        await WriteNoteAsync(Tenants.New(), "secret");

        var count = await _notes.WithoutTenantAsync(notes => notes.Notes.CountAsync(Cancellation));

        count.ShouldBe(0);
    }

    [Fact]
    public async Task WriteNote_WithoutATenant_IsRefused()
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
    public async Task WriteNote_NotCommitted_IsDiscarded()
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

    // A save of several rows would begin a transaction of its own; it joins the transaction in progress instead.
    [Fact]
    public async Task WriteNotes_SavedTogetherNotCommitted_AreAllDiscarded()
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
    public async Task DeclareTenant_InATransaction_EndsWithTheTransaction()
    {
        await using var scope = _notes.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await notes.Database.OpenConnectionAsync(Cancellation);
        await using (var transaction = await Notes.BeginUncommittedAsync(scope, Tenants.New()))
        {
            await transaction.CommitAsync(Cancellation);
        }

        var setting = await notes.Database
            .SqlQueryRaw<string?>("SELECT NULLIF(current_setting('app.tenant_id', true), '') AS \"Value\"")
            .SingleAsync(Cancellation);

        setting.ShouldBeNull();
    }

    [Fact]
    public async Task ReadRoles_OwnerAndApplication_NeitherBypassesRowLevelSecurity()
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

    private static Task<string?> UserSettingAsync(NotesDbContext notes) =>
        notes.Database.SqlQueryRaw<string?>("SELECT current_setting('app.user_id', true) AS \"Value\"").SingleAsync(Cancellation);
}

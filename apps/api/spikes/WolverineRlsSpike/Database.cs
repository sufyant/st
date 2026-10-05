using Npgsql;
using Testcontainers.PostgreSql;

namespace WolverineRlsSpike;

// Superuser-side setup. The application itself only ever connects as app_user.
public sealed class Database : IAsyncDisposable
{
    private const string SetupSql = """
        CREATE ROLE app_user LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS;

        -- Wolverine creates and owns its envelope tables; they are not RLS tables.
        CREATE SCHEMA wolverine AUTHORIZATION app_user;

        CREATE SCHEMA app;
        GRANT USAGE ON SCHEMA app TO app_user;

        CREATE TABLE app.notes (
            id uuid PRIMARY KEY,
            text text NOT NULL,
            tenant_id uuid NOT NULL DEFAULT NULLIF(current_setting('app.tenant_id', true), '')::uuid);

        CREATE TABLE app.spike_sagas (
            id uuid PRIMARY KEY,
            version int NOT NULL,
            steps int NOT NULL,
            tenant_id uuid NOT NULL DEFAULT NULLIF(current_setting('app.tenant_id', true), '')::uuid);

        -- Second schema + DbContext for test 8.
        CREATE SCHEMA audit;
        GRANT USAGE ON SCHEMA audit TO app_user;

        CREATE TABLE audit.entries (
            id uuid PRIMARY KEY,
            text text NOT NULL,
            tenant_id uuid NOT NULL DEFAULT NULLIF(current_setting('app.tenant_id', true), '')::uuid);

        GRANT SELECT, INSERT, UPDATE, DELETE ON app.notes, app.spike_sagas, audit.entries TO app_user;

        ALTER TABLE app.notes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE app.notes FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON app.notes
            USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

        ALTER TABLE app.spike_sagas ENABLE ROW LEVEL SECURITY;
        ALTER TABLE app.spike_sagas FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON app.spike_sagas
            USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

        ALTER TABLE audit.entries ENABLE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON audit.entries
            USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
        """;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public string SuperuserConnectionString => _container.GetConnectionString();

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(SuperuserConnectionString)
    {
        Username = "app_user",
        Password = "app",
    }.ConnectionString;

    public async Task StartAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync(SetupSql);
    }

    public async Task ExecuteAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(SuperuserConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        }

        await cmd.ExecuteNonQueryAsync();
    }

    // Superuser bypasses RLS, so these reads see the real stored rows.
    public async Task<T?> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(SuperuserConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var arg in args)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        }

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

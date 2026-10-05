using JasperFx;
using Npgsql;
using Tenancy;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;

namespace Api.Messaging;

// Wolverine keeps its envelopes in one shared schema (0024), over the application role's direct connection: its durability agent holds
// session-level advisory locks, which a transaction-mode pooler cannot keep (0019). A handler's own envelopes are written in its tenant
// transaction instead (TenantTransactionMiddleware). The migration step creates the schema as the owner; starting never does (0020).
internal static class MessageStorage
{
    public const string Schema = "wolverine";

    public static void Configure(WolverineOptions options, NpgsqlDataSource dataSource)
    {
        options.PersistMessagesWithPostgresql(dataSource, Schema).OverrideAutoCreateResources(AutoCreate.None);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.Policies.UseDurableLocalQueues();
    }

    // Wolverine builds its store only inside a host. This one is never started: it creates or updates the schema as the owner, with the
    // same configuration the application uses, and lets the application role use the tables.
    public static async Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(ownerConnectionString);
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddWolverine(options => Configure(options, dataSource));
        var host = builder.Build();

        try
        {
            await host.Services.GetRequiredService<IMessageStore>().Admin.MigrateAsync(AutoCreate.CreateOrUpdate);

            await using var grant = dataSource.CreateCommand(
                $"""
                GRANT USAGE ON SCHEMA {Schema} TO {DatabaseRoles.Application};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Schema} TO {DatabaseRoles.Application};
                """);
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }
}

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Tenancy;

// The one place that puts a tenant table under row level security (R1): wherever a table gets the tenant column, and for an
// existing table a migration names with IsolateTenantTable. Only TenantDbContext gives a column the tenant default, so that
// default identifies the tenant column without relying on annotations, which migration operations do not carry.
#pragma warning disable EF1001 // The Npgsql generator's only constructor takes its internal options; this class only passes them on.
internal sealed class TenantMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.INpgsqlSingletonOptions options)
    : NpgsqlMigrationsSqlGenerator(dependencies, options)
#pragma warning restore EF1001
{
    private const string Policy = "tenant_isolation";

    protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        if (operation is IsolateTenantTableOperation isolate)
        {
            IsolateTenants(isolate.Schema, isolate.Table, builder);
            return;
        }

        base.Generate(operation, model, builder);
    }

    protected override void Generate(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        base.Generate(operation, model, builder, terminate);

        if (operation.Columns.Any(IsTenantColumn))
        {
            IsolateTenants(operation.Schema, operation.Name, builder);
        }
    }

    protected override void Generate(AddColumnOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        base.Generate(operation, model, builder, terminate);

        if (IsTenantColumn(operation))
        {
            IsolateTenants(operation.Schema, operation.Table, builder);
        }
    }

    // An existing column that becomes the tenant column, as when an entity of an existing table becomes a tenant entity.
    protected override void Generate(AlterColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        base.Generate(operation, model, builder);

        if (IsTenantColumn(operation) && operation.OldColumn.DefaultValueSql != TenantColumn.CurrentTenantSql)
        {
            IsolateTenants(operation.Schema, operation.Table, builder);
        }
    }

    private static bool IsTenantColumn(ColumnOperation column) =>
        column.Name == TenantColumn.Name && column.DefaultValueSql == TenantColumn.CurrentTenantSql;

    // Forced, so the policy binds the table's owner too, which runs the migrations (R1). The policy is replaced if it exists, so an
    // existing tenant table ends with exactly the policy a new one gets.
    private void IsolateTenants(string? schema, string table, MigrationCommandListBuilder builder)
    {
        var helper = Dependencies.SqlGenerationHelper;
        var qualifiedTable = helper.DelimitIdentifier(table, schema);
        var belongsToCurrentTenant = $"{helper.DelimitIdentifier(TenantColumn.Name)} = {TenantColumn.CurrentTenantSql}";

        builder
            .Append($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY")
            .AppendLine(helper.StatementTerminator)
            .EndCommand();

        builder
            .Append($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY")
            .AppendLine(helper.StatementTerminator)
            .EndCommand();

        builder
            .Append($"DROP POLICY IF EXISTS {helper.DelimitIdentifier(Policy)} ON {qualifiedTable}")
            .AppendLine(helper.StatementTerminator)
            .EndCommand();

        builder
            .Append($"CREATE POLICY {helper.DelimitIdentifier(Policy)} ON {qualifiedTable} ")
            .Append($"USING ({belongsToCurrentTenant}) WITH CHECK ({belongsToCurrentTenant})")
            .AppendLine(helper.StatementTerminator)
            .EndCommand();
    }
}

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Tenancy;

// Adds the row level security policy wherever a table gets the tenant column (0014). Only TenantDbContext gives a column the
// tenant default, so that default identifies the tenant column without relying on annotations, which migration operations
// do not carry.
#pragma warning disable EF1001 // The Npgsql generator's only constructor takes its internal options; this class only passes them on.
internal sealed class TenantMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.INpgsqlSingletonOptions options)
    : NpgsqlMigrationsSqlGenerator(dependencies, options)
#pragma warning restore EF1001
{
    private const string Policy = "tenant_isolation";

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

    private static bool IsTenantColumn(ColumnOperation column) =>
        column.Name == TenantColumn.Name && column.DefaultValueSql == TenantColumn.CurrentTenantSql;

    // Not forced: tables stay visible to their owner, which runs the migrations and owns the narrow SECURITY DEFINER lookups
    // (0017). The application role owns no table, so the policy always applies to it.
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
            .Append($"CREATE POLICY {helper.DelimitIdentifier(Policy)} ON {qualifiedTable} ")
            .Append($"USING ({belongsToCurrentTenant}) WITH CHECK ({belongsToCurrentTenant})")
            .AppendLine(helper.StatementTerminator)
            .EndCommand();
    }
}

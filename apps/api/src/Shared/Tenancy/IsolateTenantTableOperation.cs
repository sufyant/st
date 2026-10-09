using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Tenancy;

/// <summary>
/// Puts an existing tenant table under the same row level security a new one gets from <see cref="TenantMigrationsSqlGenerator"/>,
/// for a table whose model did not change, so no other operation would reach it.
/// </summary>
public sealed class IsolateTenantTableOperation : MigrationOperation
{
    public required string Table { get; init; }

    public string? Schema { get; init; }
}

public static class IsolateTenantTableMigrationBuilderExtensions
{
    public static OperationBuilder<IsolateTenantTableOperation> IsolateTenantTable(this MigrationBuilder migrationBuilder, string table, string? schema = null)
    {
        var operation = new IsolateTenantTableOperation { Table = table, Schema = schema };
        migrationBuilder.Operations.Add(operation);

        return new OperationBuilder<IsolateTenantTableOperation>(operation);
    }
}

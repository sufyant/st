using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            // Privileges come from migrations (0018). Audit entries are never changed once written (0040), so the application role
            // may only add and read them.
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA audit TO api_application;
                ALTER DEFAULT PRIVILEGES IN SCHEMA audit GRANT SELECT, INSERT ON TABLES TO api_application;
                """);

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_occurred_at",
                schema: "audit",
                table: "entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id",
                schema: "audit",
                table: "entries",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");

            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA audit REVOKE SELECT, INSERT ON TABLES FROM api_application;
                REVOKE USAGE ON SCHEMA audit FROM api_application;
                """);
        }
    }
}

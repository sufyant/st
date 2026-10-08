using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            // Privileges come from migrations (0018): the application role works with the catalog.
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA catalog TO api_application;
                ALTER DEFAULT PRIVILEGES IN SCHEMA catalog GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO api_application;
                """);

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "catalog",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => new { x.tenant_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                schema: "catalog",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "catalog",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_external_id",
                schema: "catalog",
                table: "users",
                column: "external_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memberships",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "users",
                schema: "catalog");

            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA catalog REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM api_application;
                REVOKE USAGE ON SCHEMA catalog FROM api_application;
                """);
        }
    }
}

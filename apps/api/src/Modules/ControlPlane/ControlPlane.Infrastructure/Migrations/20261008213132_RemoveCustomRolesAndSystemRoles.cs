using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomRolesAndSystemRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Custom roles and the Viewer role go. Memberships and invitations that hold one move to Member, which now holds the same
            // permissions as Viewer did: none.
            migrationBuilder.Sql("""
                UPDATE catalog.memberships SET role_id = '00000000-0000-7000-8000-000000000003'
                WHERE role_id IN (SELECT id FROM catalog.roles WHERE built_in IS NULL OR built_in = 'Viewer');
                UPDATE catalog.invitations SET role_id = '00000000-0000-7000-8000-000000000003'
                WHERE role_id IN (SELECT id FROM catalog.roles WHERE built_in IS NULL OR built_in = 'Viewer');
                DELETE FROM catalog.roles WHERE built_in IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_roles_tenants_tenant_id",
                schema: "catalog",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_name",
                schema: "catalog",
                table: "roles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_roles_built_in_xor_tenant",
                schema: "catalog",
                table: "roles");

            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-7000-8000-000000000004"));

            migrationBuilder.DropColumn(
                name: "role",
                schema: "catalog",
                table: "system_admins");

            migrationBuilder.DropColumn(
                name: "permissions",
                schema: "catalog",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "roles");

            migrationBuilder.AlterColumn<string>(
                name: "built_in",
                schema: "catalog",
                table: "roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                schema: "catalog",
                table: "system_admins",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Administrator");

            migrationBuilder.AlterColumn<string>(
                name: "built_in",
                schema: "catalog",
                table: "roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string[]>(
                name: "permissions",
                schema: "catalog",
                table: "roles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "catalog",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-7000-8000-000000000001"),
                columns: new[] { "tenant_id", "permissions" },
                values: new object[] { null, new string[0] });

            migrationBuilder.UpdateData(
                schema: "catalog",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-7000-8000-000000000002"),
                columns: new[] { "tenant_id", "permissions" },
                values: new object[] { null, new string[0] });

            migrationBuilder.UpdateData(
                schema: "catalog",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-7000-8000-000000000003"),
                columns: new[] { "tenant_id", "permissions" },
                values: new object[] { null, new string[0] });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "roles",
                columns: new[] { "id", "built_in", "name", "tenant_id", "permissions" },
                values: new object[] { new Guid("00000000-0000-7000-8000-000000000004"), "Viewer", "Viewer", null, new string[0] });

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                schema: "catalog",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_roles_built_in_xor_tenant",
                schema: "catalog",
                table: "roles",
                sql: "(built_in IS NULL) <> (tenant_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_roles_tenants_tenant_id",
                schema: "catalog",
                table: "roles",
                column: "tenant_id",
                principalSchema: "catalog",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

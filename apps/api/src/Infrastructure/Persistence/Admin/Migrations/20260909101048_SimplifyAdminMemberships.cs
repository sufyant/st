using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Admin.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyAdminMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_memberships_roles_role_id",
                schema: "admin",
                table: "memberships");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "admin");

            migrationBuilder.DropIndex(
                name: "IX_memberships_role_id",
                schema: "admin",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "admin",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "admin",
                table: "memberships");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "admin",
                table: "memberships",
                newName: "external_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_memberships_tenant_id_user_id",
                schema: "admin",
                table: "memberships",
                newName: "IX_memberships_tenant_id_external_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "external_user_id",
                schema: "admin",
                table: "memberships",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_memberships_tenant_id_external_user_id",
                schema: "admin",
                table: "memberships",
                newName: "IX_memberships_tenant_id_user_id");

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "admin",
                table: "memberships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "admin",
                table: "memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_roles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "admin",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_role_id",
                schema: "admin",
                table: "memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_roles_tenant_id_name",
                schema: "admin",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_memberships_roles_role_id",
                schema: "admin",
                table: "memberships",
                column: "role_id",
                principalSchema: "admin",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

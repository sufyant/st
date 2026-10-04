using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Memberships made before roles existed get the least privileged built-in role; new ones always name their role.
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "catalog",
                table: "memberships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000003"));

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    built_in = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.CheckConstraint("ck_roles_built_in_xor_tenant", "(built_in IS NULL) <> (tenant_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_roles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_admins",
                schema: "catalog",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_admins", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_system_admins_users_granted_by",
                        column: x => x.granted_by,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_system_admins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitations_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "catalog",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitations_users_accepted_by",
                        column: x => x.accepted_by,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_users_invited_by",
                        column: x => x.invited_by,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "roles",
                columns: new[] { "id", "built_in", "name", "tenant_id", "permissions" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-7000-8000-000000000001"), "Owner", "Owner", null, new string[0] },
                    { new Guid("00000000-0000-7000-8000-000000000002"), "Admin", "Admin", null, new string[0] },
                    { new Guid("00000000-0000-7000-8000-000000000003"), "Member", "Member", null, new string[0] },
                    { new Guid("00000000-0000-7000-8000-000000000004"), "Viewer", "Viewer", null, new string[0] }
                });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_role_id",
                schema: "catalog",
                table: "memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_accepted_by",
                schema: "catalog",
                table: "invitations",
                column: "accepted_by");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_invited_by",
                schema: "catalog",
                table: "invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_role_id",
                schema: "catalog",
                table: "invitations",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_tenant_id",
                schema: "catalog",
                table: "invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token_hash",
                schema: "catalog",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                schema: "catalog",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_admins_granted_by",
                schema: "catalog",
                table: "system_admins",
                column: "granted_by");

            migrationBuilder.AddForeignKey(
                name: "fk_memberships_roles_role_id",
                schema: "catalog",
                table: "memberships",
                column: "role_id",
                principalSchema: "catalog",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("ALTER TABLE catalog.memberships ALTER COLUMN role_id DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_memberships_roles_role_id",
                schema: "catalog",
                table: "memberships");

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "system_admins",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "ix_memberships_role_id",
                schema: "catalog",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "catalog",
                table: "memberships");
        }
    }
}

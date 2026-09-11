using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenantAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[,]
                {
                    { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), "members.read", "View tenant members.", "View members" },
                    { new Guid("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"), "members.manage", "Add and remove tenant members.", "Manage members" },
                    { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), "roles.read", "View tenant roles and their permissions.", "View roles" },
                    { new Guid("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"), "roles.manage", "Create and edit tenant roles.", "Manage roles" },
                    { new Guid("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"), "invitations.manage", "Create and revoke tenant invitations.", "Manage invitations" }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[,]
                {
                    { new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310"), "member", "Read-only access to this tenant.", "Member" },
                    { new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124"), "owner", "Full access to this tenant.", "Owner" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") },
                    { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") },
                    { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_roles_code",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_external_user_id",
                table: "users",
                column: "external_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

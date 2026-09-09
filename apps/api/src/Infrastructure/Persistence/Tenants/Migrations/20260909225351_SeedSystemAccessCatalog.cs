using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class SeedSystemAccessCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                values: new object[] { new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124"), "owner", "Full access to this tenant.", "Owner" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") },
                    { new Guid("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"), new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124") });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("1a7bd5bd-0e5f-42aa-b0d9-a8144ea96574"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("3c8c6534-6ca7-4b5f-93c5-1ba3c6d0a0b9"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("4d1b2a8e-8cf1-41a2-9d57-e5123e4ca92f"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("e8d5f5ca-1b85-4ca3-99d2-a8534c9dc124"));
        }
    }
}

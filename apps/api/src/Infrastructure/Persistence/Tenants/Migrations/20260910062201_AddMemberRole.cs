using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[] { new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310"), "member", "Read-only access to this tenant.", "Member" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") },
                    { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("0f81cb90-d5a9-4c64-8b6f-a50378e81970"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("292c9f17-f4c3-48b9-b66a-8de6f2d1e5b0"), new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310") });

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("5b2c1a44-9d3e-4f81-b0a7-6c8e2f95d310"));
        }
    }
}

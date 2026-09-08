using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSchemaForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId",
                schema: "admin",
                table: "Memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenantId",
                schema: "admin",
                table: "Invitations",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Tenants_TenantId",
                schema: "admin",
                table: "Invitations",
                column: "TenantId",
                principalSchema: "admin",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Tenants_TenantId",
                schema: "admin",
                table: "Memberships",
                column: "TenantId",
                principalSchema: "admin",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Users_UserId",
                schema: "admin",
                table: "Memberships",
                column: "UserId",
                principalSchema: "admin",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Tenants_TenantId",
                schema: "admin",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Tenants_TenantId",
                schema: "admin",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Users_UserId",
                schema: "admin",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId",
                schema: "admin",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_TenantId",
                schema: "admin",
                table: "Invitations");
        }
    }
}

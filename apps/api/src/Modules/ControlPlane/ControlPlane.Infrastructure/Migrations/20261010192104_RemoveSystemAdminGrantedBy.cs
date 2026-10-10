using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSystemAdminGrantedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_system_admins_users_granted_by",
                schema: "catalog",
                table: "system_admins");

            migrationBuilder.DropIndex(
                name: "ix_system_admins_granted_by",
                schema: "catalog",
                table: "system_admins");

            migrationBuilder.DropColumn(
                name: "granted_by",
                schema: "catalog",
                table: "system_admins");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "granted_by",
                schema: "catalog",
                table: "system_admins",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_admins_granted_by",
                schema: "catalog",
                table: "system_admins",
                column: "granted_by");

            migrationBuilder.AddForeignKey(
                name: "fk_system_admins_users_granted_by",
                schema: "catalog",
                table: "system_admins",
                column: "granted_by",
                principalSchema: "catalog",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

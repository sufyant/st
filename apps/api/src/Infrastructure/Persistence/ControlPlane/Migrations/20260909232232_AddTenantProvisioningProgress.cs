using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.ControlPlane.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProvisioningProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provisioning_error",
                schema: "control",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provisioning_step",
                schema: "control",
                table: "tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provisioning_error",
                schema: "control",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "provisioning_step",
                schema: "control",
                table: "tenants");
        }
    }
}

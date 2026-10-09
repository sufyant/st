using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Failed status is gone: an onboarding that does not finish cancels its tenant. One that failed before has no reason.
            migrationBuilder.Sql("UPDATE catalog.tenants SET status = 'Cancelled' WHERE status = 'Failed';");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "catalog",
                table: "tenants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_onboardings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_provider_invitation_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    invitation_email_timeout = table.Column<TimeSpan>(type: "interval", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid"),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_onboardings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_onboardings_tenant_id",
                schema: "catalog",
                table: "tenant_onboardings",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_onboardings",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "catalog",
                table: "tenants");

            migrationBuilder.Sql("UPDATE catalog.tenants SET status = 'Failed' WHERE status = 'Cancelled';");
        }
    }
}

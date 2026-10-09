using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantCreationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_creation_requests",
                schema: "catalog",
                columns: table => new
                {
                    system_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_creation_requests", x => new { x.system_admin_id, x.idempotency_key });
                    table.ForeignKey(
                        name: "fk_tenant_creation_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tenant_creation_requests_users_system_admin_id",
                        column: x => x.system_admin_id,
                        principalSchema: "catalog",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_creation_requests_tenant_id",
                schema: "catalog",
                table: "tenant_creation_requests",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_creation_requests",
                schema: "catalog");
        }
    }
}

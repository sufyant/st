using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Tenancy;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            // Privileges come from migrations (0018).
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA notifications TO api_application;
                ALTER DEFAULT PRIVILEGES IN SCHEMA notifications GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO api_application;
                """);

            migrationBuilder.CreateTable(
                name: "scheduled_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_notifications_recipient_id",
                schema: "notifications",
                table: "scheduled_notifications",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_notifications_status_due_at",
                schema: "notifications",
                table: "scheduled_notifications",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_notifications_tenant_id",
                schema: "notifications",
                table: "scheduled_notifications",
                column: "tenant_id");

            // The scanner's narrow lookup across tenants: the tenant and id of every due notification, nothing else (0017).
            migrationBuilder.Sql(TenantScan.CreateFunctionSql(
                "notifications",
                "due_scheduled_notifications",
                "due_at_or_before timestamptz",
                "scheduled_notifications",
                "scanned.status = 'Scheduled' AND scanned.due_at <= due_at_or_before"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION notifications.due_scheduled_notifications(timestamptz);");

            migrationBuilder.DropTable(
                name: "scheduled_notifications",
                schema: "notifications");

            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA notifications REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM api_application;
                REVOKE USAGE ON SCHEMA notifications FROM api_application;
                """);
        }
    }
}

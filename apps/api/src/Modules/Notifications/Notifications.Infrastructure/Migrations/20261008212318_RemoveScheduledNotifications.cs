using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveScheduledNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION notifications.due_scheduled_notifications(timestamptz);");

            migrationBuilder.DropTable(
                name: "scheduled_notifications",
                schema: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "scheduled_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid"),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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

            migrationBuilder.Sql("""
                CREATE FUNCTION "notifications"."due_scheduled_notifications"(due_at_or_before timestamptz)
                RETURNS TABLE (tenant_id uuid, id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $scan$
                    SELECT scanned.tenant_id, scanned.id FROM "notifications"."scheduled_notifications" AS scanned WHERE scanned.status = 'Scheduled' AND scanned.due_at <= due_at_or_before
                $scan$;
                REVOKE ALL ON FUNCTION "notifications"."due_scheduled_notifications"(due_at_or_before timestamptz) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION "notifications"."due_scheduled_notifications"(due_at_or_before timestamptz) TO api_application;
                """);
        }
    }
}

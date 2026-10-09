using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IsolateMembershipsAndInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Expired status is gone; expiry is checked when an invitation is read, so these rows stay unusable. This runs
            // first: once row level security is forced it binds this owner too, and without a tenant it would change no row.
            migrationBuilder.Sql("UPDATE catalog.invitations SET status = 'Pending' WHERE status = 'Expired';");

            // R1: the tenant column default makes both tables tenant tables, and the SQL generator enables and forces row level
            // security on them with the tenant isolation policy.
            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "memberships",
                type: "uuid",
                nullable: false,
                defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "invitations",
                type: "uuid",
                nullable: false,
                defaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An invitation that was Expired stays Pending; the earlier code checks expiry when it reads one too.
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON catalog.memberships;
                ALTER TABLE catalog.memberships NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE catalog.memberships DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant_isolation ON catalog.invitations;
                ALTER TABLE catalog.invitations NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE catalog.invitations DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "memberships",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "invitations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "NULLIF(current_setting('app.tenant_id', true), '')::uuid");
        }
    }
}

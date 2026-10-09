using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnMembershipsPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R11: GET /v1/me/tenants runs without a tenant, with the catalog user declared instead, and reads that user's memberships
            // in every tenant. The only hand-written policy. It is FOR SELECT only: permissive policies combine with OR, so a policy
            // that allowed a write here would let a user add themselves to any tenant.
            migrationBuilder.Sql(
                "CREATE POLICY own_memberships ON catalog.memberships FOR SELECT "
                + "USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY own_memberships ON catalog.memberships;");
        }
    }
}

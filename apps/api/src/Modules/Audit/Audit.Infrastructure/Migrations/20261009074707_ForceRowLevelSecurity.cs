using Microsoft.EntityFrameworkCore.Migrations;
using Tenancy;

#nullable disable

namespace Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ForceRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R1: the table had row level security enabled but not forced, so it did not bind its owner. The SQL generator
            // forces it and puts back the same tenant isolation policy.
            migrationBuilder.IsolateTenantTable("entries", "audit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE audit.entries NO FORCE ROW LEVEL SECURITY;");
        }
    }
}

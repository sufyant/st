using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SignUpTheOwnerLikeAnyUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "identity_provider_invitation_id",
                schema: "catalog",
                table: "tenant_onboardings");

            // An onboarding already under way waits for a cancellation as long as the setting's default.
            migrationBuilder.AddColumn<TimeSpan>(
                name: "cancellation_timeout",
                schema: "catalog",
                table: "tenant_onboardings",
                type: "interval",
                nullable: false,
                defaultValue: TimeSpan.FromMinutes(10));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_timeout",
                schema: "catalog",
                table: "tenant_onboardings");

            migrationBuilder.AddColumn<string>(
                name: "identity_provider_invitation_id",
                schema: "catalog",
                table: "tenant_onboardings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}

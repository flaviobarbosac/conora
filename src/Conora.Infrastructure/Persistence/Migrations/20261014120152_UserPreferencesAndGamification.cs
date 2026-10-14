using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPreferencesAndGamification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeAbsoluteValuesInRetrospective",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MonthCloseDay",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Existing accounts already past first entry — skip forced onboarding.
            migrationBuilder.AddColumn<bool>(
                name: "OnboardingCompleted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuietMode",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReserveTargetMonths",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<string>(
                name: "ThemeId",
                table: "users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "clareza");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeAbsoluteValuesInRetrospective",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MonthCloseDay",
                table: "users");

            migrationBuilder.DropColumn(
                name: "OnboardingCompleted",
                table: "users");

            migrationBuilder.DropColumn(
                name: "QuietMode",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ReserveTargetMonths",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "users");
        }
    }
}

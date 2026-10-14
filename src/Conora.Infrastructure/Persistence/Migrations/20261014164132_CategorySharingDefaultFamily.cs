using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategorySharingDefaultFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Opt-out model: existing chart becomes Family so solo users see the full budget
            // and joining a group already carries accounts into consolidation.
            migrationBuilder.Sql("""
                UPDATE categories
                SET "SharingScope" = 'Family'
                WHERE "SharingScope" = 'Personal'
                  AND "DeletedAt" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "SharingScope",
                table: "categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Family",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Personal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SharingScope",
                table: "categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Personal",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Family");
        }
    }
}

using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// New accounts default to a 30-day trial. Existing users without an approved Hotmart
    /// course purchase lose the accidental 90-day flag from the previous default.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261026120000_TrainingStudentDefaultFalse")]
    public partial class TrainingStudentDefaultFalse : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsTrainingStudent",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.Sql(
                """
                UPDATE users AS u
                SET "IsTrainingStudent" = false
                WHERE u."IsTrainingStudent" = true
                  AND NOT EXISTS (
                    SELECT 1
                    FROM hotmart_course_purchases AS h
                    WHERE h."Email" = u."Email"
                      AND h."Approved" = true
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsTrainingStudent",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }
    }
}

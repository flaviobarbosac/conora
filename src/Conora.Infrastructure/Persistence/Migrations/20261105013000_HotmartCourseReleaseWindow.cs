using System;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261105013000_HotmartCourseReleaseWindow")]
    public partial class HotmartCourseReleaseWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcquiredAt",
                table: "hotmart_course_purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseEmailSentAt",
                table: "hotmart_course_purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "hotmart_course_purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockAt",
                table: "hotmart_course_purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE hotmart_course_purchases
                SET "AcquiredAt" = "ReceivedAt"
                WHERE "AcquiredAt" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AcquiredAt",
                table: "hotmart_course_purchases",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AcquiredAt", table: "hotmart_course_purchases");
            migrationBuilder.DropColumn(name: "ReleaseEmailSentAt", table: "hotmart_course_purchases");
            migrationBuilder.DropColumn(name: "RevokedAt", table: "hotmart_course_purchases");
            migrationBuilder.DropColumn(name: "UnlockAt", table: "hotmart_course_purchases");
        }
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117020000_UserAccessGrant")]
    public partial class UserAccessGrant : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAtUtc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ComplimentaryAccessIndefinite",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ComplimentaryAccessUntilUtc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TrialEndsAtUtc", table: "users");
            migrationBuilder.DropColumn(name: "ComplimentaryAccessIndefinite", table: "users");
            migrationBuilder.DropColumn(name: "ComplimentaryAccessUntilUtc", table: "users");
        }
    }
}

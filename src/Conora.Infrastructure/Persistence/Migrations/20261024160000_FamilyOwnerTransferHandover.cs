using System;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261024160000_FamilyOwnerTransferHandover")]
    public partial class FamilyOwnerTransferHandover : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizedAt",
                table: "family_owner_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BudgetsMovedAt",
                table: "family_owner_transfers",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthorizedAt", table: "family_owner_transfers");
            migrationBuilder.DropColumn(name: "BudgetsMovedAt", table: "family_owner_transfers");
        }
    }
}

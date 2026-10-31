using System;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030210000_PreRegistrationReuseWindow")]
public partial class PreRegistrationReuseWindow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "FirstConsumedAtUtc",
            table: "pre_registrations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PromotedUserId",
            table: "pre_registrations",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FirstConsumedAtUtc",
            table: "pre_registrations");

        migrationBuilder.DropColumn(
            name: "PromotedUserId",
            table: "pre_registrations");
    }
}

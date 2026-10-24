using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HotmartCoursePurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hotmart_course_purchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Transaction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: true),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hotmart_course_purchases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HotmartCoursePurchases_Email",
                table: "hotmart_course_purchases",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_HotmartCoursePurchases_Transaction",
                table: "hotmart_course_purchases",
                column: "Transaction",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hotmart_course_purchases");
        }
    }
}

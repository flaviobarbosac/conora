using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LgpdExportStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DownloadConsumedAtUtc",
                table: "lgpd_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadTokenHash",
                table: "lgpd_requests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "lgpd_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExportObjectKey",
                table: "lgpd_requests",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lgpd_requests_DownloadTokenHash",
                table: "lgpd_requests",
                column: "DownloadTokenHash",
                unique: true,
                filter: "\"DownloadTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lgpd_requests_DownloadTokenHash",
                table: "lgpd_requests");

            migrationBuilder.DropColumn(
                name: "DownloadConsumedAtUtc",
                table: "lgpd_requests");

            migrationBuilder.DropColumn(
                name: "DownloadTokenHash",
                table: "lgpd_requests");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "lgpd_requests");

            migrationBuilder.DropColumn(
                name: "ExportObjectKey",
                table: "lgpd_requests");
        }
    }
}

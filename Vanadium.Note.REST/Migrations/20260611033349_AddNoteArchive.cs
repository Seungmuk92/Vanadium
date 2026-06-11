using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchiveRoot",
                table: "Notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_ArchivedAt",
                table: "Notes",
                column: "ArchivedAt",
                filter: "\"ArchivedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_ArchivedAt",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "IsArchiveRoot",
                table: "Notes");
        }
    }
}

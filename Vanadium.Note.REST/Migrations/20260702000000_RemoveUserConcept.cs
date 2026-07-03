using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserConcept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the foreign keys pointing at Users first so the columns and the
            // Users table itself can be removed.
            migrationBuilder.DropForeignKey(
                name: "FK_ApiTokens_Users_UserId",
                table: "ApiTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_Labels_Users_UserId",
                table: "Labels");

            migrationBuilder.DropForeignKey(
                name: "FK_LabelCategories_Users_UserId",
                table: "LabelCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Users_UserId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_ApiTokens_UserId",
                table: "ApiTokens");

            migrationBuilder.DropIndex(
                name: "IX_Labels_UserId",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_LabelCategories_UserId",
                table: "LabelCategories");

            migrationBuilder.DropIndex(
                name: "IX_Notes_UserId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_UserSettings_Username",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Labels");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "LabelCategories");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "UserSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Structural rollback only. The original owning-user rows and per-row
            // UserId values cannot be recovered — columns come back with an empty
            // GUID default and Username with an empty string.
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "LabelCategories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Notes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "UserSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                table: "ApiTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_UserId",
                table: "Labels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabelCategories_UserId",
                table: "LabelCategories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId",
                table: "Notes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_Username",
                table: "UserSettings",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiTokens_Users_UserId",
                table: "ApiTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Users_UserId",
                table: "Labels",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LabelCategories_Users_UserId",
                table: "LabelCategories",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Users_UserId",
                table: "Notes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

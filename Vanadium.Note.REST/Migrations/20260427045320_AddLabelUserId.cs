using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add as nullable first so existing rows can be populated
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Labels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "LabelCategories",
                type: "uuid",
                nullable: true);

            // Assign all existing labels and categories to the 'smoh' user
            migrationBuilder.Sql("""
                UPDATE "Labels"
                SET "UserId" = u."Id"
                FROM "Users" u
                WHERE u."Username" = 'smoh'
                """);

            migrationBuilder.Sql("""
                UPDATE "LabelCategories"
                SET "UserId" = u."Id"
                FROM "Users" u
                WHERE u."Username" = 'smoh'
                """);

            // Now enforce NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Labels",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "LabelCategories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Labels_UserId",
                table: "Labels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabelCategories_UserId",
                table: "LabelCategories",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LabelCategories_Users_UserId",
                table: "LabelCategories",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Labels_Users_UserId",
                table: "Labels",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabelCategories_Users_UserId",
                table: "LabelCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Labels_Users_UserId",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_Labels_UserId",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_LabelCategories_UserId",
                table: "LabelCategories");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Labels");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "LabelCategories");
        }
    }
}

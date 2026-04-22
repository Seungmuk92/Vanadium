using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddContentTextFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentText",
                table: "Notes",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Backfill existing notes by stripping HTML tags
            migrationBuilder.Sql("""
                UPDATE "Notes"
                SET "ContentText" = trim(regexp_replace("Content", '<[^>]+>', ' ', 'g'))
                WHERE "ContentText" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes",
                columns: new[] { "Title", "ContentText" })
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "simple");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "ContentText",
                table: "Notes");
        }
    }
}

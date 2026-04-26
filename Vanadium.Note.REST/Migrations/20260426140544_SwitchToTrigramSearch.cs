using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");

            migrationBuilder.DropIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes",
                columns: new[] { "Title", "ContentText" })
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Title_ContentText",
                table: "Notes",
                columns: new[] { "Title", "ContentText" })
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "simple");
        }
    }
}

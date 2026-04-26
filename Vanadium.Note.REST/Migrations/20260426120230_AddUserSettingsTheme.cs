using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettingsTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "UserSettings",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "system");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Theme",
                table: "UserSettings");
        }
    }
}

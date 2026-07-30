using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertyOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyOptions", x => x.Id);
                    table.UniqueConstraint("AK_PropertyOptions_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.ForeignKey(
                        name: "FK_PropertyOptions_PropertyDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "PropertyDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotePropertyValues",
                columns: table => new
                {
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NumberValue = table.Column<double>(type: "double precision", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    BoolValue = table.Column<bool>(type: "boolean", nullable: true),
                    SelectedOptionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotePropertyValues", x => new { x.NoteId, x.DefinitionId });
                    table.ForeignKey(
                        name: "FK_NotePropertyValues_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotePropertyValues_PropertyDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "PropertyDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotePropertyValues_PropertyOptions_DefinitionId_SelectedOpt~",
                        columns: x => new { x.DefinitionId, x.SelectedOptionId },
                        principalTable: "PropertyOptions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotePropertySelectedOptions",
                columns: table => new
                {
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotePropertySelectedOptions", x => new { x.NoteId, x.DefinitionId, x.OptionId });
                    table.ForeignKey(
                        name: "FK_NotePropertySelectedOptions_NotePropertyValues_NoteId_Defin~",
                        columns: x => new { x.NoteId, x.DefinitionId },
                        principalTable: "NotePropertyValues",
                        principalColumns: new[] { "NoteId", "DefinitionId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotePropertySelectedOptions_PropertyOptions_DefinitionId_Op~",
                        columns: x => new { x.DefinitionId, x.OptionId },
                        principalTable: "PropertyOptions",
                        principalColumns: new[] { "DefinitionId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertySelectedOptions_DefinitionId_OptionId",
                table: "NotePropertySelectedOptions",
                columns: new[] { "DefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertySelectedOptions_OptionId",
                table: "NotePropertySelectedOptions",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertyValues_DefinitionId_BoolValue",
                table: "NotePropertyValues",
                columns: new[] { "DefinitionId", "BoolValue" });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertyValues_DefinitionId_DateValue",
                table: "NotePropertyValues",
                columns: new[] { "DefinitionId", "DateValue" });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertyValues_DefinitionId_NumberValue",
                table: "NotePropertyValues",
                columns: new[] { "DefinitionId", "NumberValue" });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertyValues_DefinitionId_SelectedOptionId",
                table: "NotePropertyValues",
                columns: new[] { "DefinitionId", "SelectedOptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotePropertyValues_DefinitionId_TextValue",
                table: "NotePropertyValues",
                columns: new[] { "DefinitionId", "TextValue" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotePropertySelectedOptions");

            migrationBuilder.DropTable(
                name: "NotePropertyValues");

            migrationBuilder.DropTable(
                name: "PropertyOptions");

            migrationBuilder.DropTable(
                name: "PropertyDefinitions");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vanadium.Note.REST.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteGroupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArchiveGroupId",
                table: "Notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletionGroupId",
                table: "Notes",
                type: "uuid",
                nullable: true);

            // Backfill existing groups so the switch from timestamp-equality to group-id
            // equality is lossless: every note already in a group gets a group id, and all
            // notes that previously grouped by a shared timestamp keep grouping together.
            // One fresh uuid is assigned per distinct DeletedAt / ArchivedAt value
            // (gen_random_uuid() is evaluated once per GROUP BY row). Active notes keep NULL.
            migrationBuilder.Sql(@"
                UPDATE ""Notes"" AS n
                SET ""DeletionGroupId"" = sub.gid
                FROM (
                    SELECT ""DeletedAt"" AS ts, gen_random_uuid() AS gid
                    FROM ""Notes""
                    WHERE ""DeletedAt"" IS NOT NULL
                    GROUP BY ""DeletedAt""
                ) AS sub
                WHERE n.""DeletedAt"" = sub.ts;");

            migrationBuilder.Sql(@"
                UPDATE ""Notes"" AS n
                SET ""ArchiveGroupId"" = sub.gid
                FROM (
                    SELECT ""ArchivedAt"" AS ts, gen_random_uuid() AS gid
                    FROM ""Notes""
                    WHERE ""ArchivedAt"" IS NOT NULL
                    GROUP BY ""ArchivedAt""
                ) AS sub
                WHERE n.""ArchivedAt"" = sub.ts;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveGroupId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "DeletionGroupId",
                table: "Notes");
        }
    }
}

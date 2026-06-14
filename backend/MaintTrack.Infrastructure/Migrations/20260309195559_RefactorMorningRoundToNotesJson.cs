using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorMorningRoundToNotesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS morning_round_items CASCADE;");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'morning_round_reports' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE morning_round_reports
            ADD COLUMN IF NOT EXISTS notes_json text NOT NULL DEFAULT '';
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notes_json",
                table: "morning_round_reports");

            migrationBuilder.CreateTable(
                name: "morning_round_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_checked = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_morning_round_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_morning_round_items_morning_round_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "morning_round_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_morning_round_items_report_id",
                table: "morning_round_items",
                column: "report_id");
        }
    }
}

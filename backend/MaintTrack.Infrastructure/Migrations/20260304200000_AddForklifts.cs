using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForklifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forklifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forklifts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forklift_reports_forklift_id",
                table: "forklift_reports",
                column: "forklift_id");

            migrationBuilder.AddForeignKey(
                name: "FK_forklift_reports_forklifts_forklift_id",
                table: "forklift_reports",
                column: "forklift_id",
                principalTable: "forklifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_forklift_reports_forklifts_forklift_id",
                table: "forklift_reports");

            migrationBuilder.DropIndex(
                name: "IX_forklift_reports_forklift_id",
                table: "forklift_reports");

            migrationBuilder.DropTable(
                name: "forklifts");
        }
    }
}

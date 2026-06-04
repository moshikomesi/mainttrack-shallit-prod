using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForkliftReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forklift_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forklift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forklift_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "forklift_treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    technician = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forklift_treatments", x => x.id);
                    table.ForeignKey(
                        name: "FK_forklift_treatments_forklift_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "forklift_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forklift_faults",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fault_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    repair_cost = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forklift_faults", x => x.id);
                    table.ForeignKey(
                        name: "FK_forklift_faults_forklift_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "forklift_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forklift_inspections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forklift_inspections", x => x.id);
                    table.ForeignKey(
                        name: "FK_forklift_inspections_forklift_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "forklift_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forklift_treatments_report_id",
                table: "forklift_treatments",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "IX_forklift_faults_report_id",
                table: "forklift_faults",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "IX_forklift_inspections_report_id",
                table: "forklift_inspections",
                column: "report_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forklift_treatments");

            migrationBuilder.DropTable(
                name: "forklift_faults");

            migrationBuilder.DropTable(
                name: "forklift_inspections");

            migrationBuilder.DropTable(
                name: "forklift_reports");
        }
    }
}

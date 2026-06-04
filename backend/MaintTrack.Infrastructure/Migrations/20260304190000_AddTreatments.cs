using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    treatment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    treatment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    technician = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cost = table.Column<decimal>(type: "numeric", nullable: false),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treatments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_treatments_tenant_id_equipment_type",
                table: "treatments",
                columns: new[] { "tenant_id", "equipment_type" });

            migrationBuilder.CreateIndex(
                name: "IX_treatments_tenant_id_treatment_date",
                table: "treatments",
                columns: new[] { "tenant_id", "treatment_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "treatments");
        }
    }
}

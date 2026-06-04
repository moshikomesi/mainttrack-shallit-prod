using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations;

public class MaintenanceTypesWithCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "maintenance_types",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_maintenance_types", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_types_tenant_id",
            table: "maintenance_types",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_types_tenant_id_code",
            table: "maintenance_types",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.AddColumn<Guid>(
            name: "MaintenanceTypeId",
            table: "MaintenanceEntries",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceEntries_MaintenanceTypeId",
            table: "MaintenanceEntries",
            column: "MaintenanceTypeId");

        migrationBuilder.AddForeignKey(
            name: "FK_MaintenanceEntries_maintenance_types_MaintenanceTypeId",
            table: "MaintenanceEntries",
            column: "MaintenanceTypeId",
            principalTable: "maintenance_types",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        // ✅ seed לפי ה־frontend (מותאם 1:1 ל-translations)
        migrationBuilder.Sql(
            """
            INSERT INTO maintenance_types (id, tenant_id, code, is_active, created_at)
            SELECT gen_random_uuid(), t.id, v.code, true, NOW()
            FROM tenants t
            CROSS JOIN (VALUES
              ('shaft'),
              ('conveyor_belt'),
              ('bearing'),
              ('screen'),
              ('calibration_cells'),
              ('pistons'),
              ('motor'),
              ('sensor'),
              ('heating_element'),
              ('thermostat'),
              ('blade'),
              ('timing_belt'),
              ('rail_cart'),
              ('other')
            ) AS v(code)
            WHERE NOT EXISTS (
              SELECT 1 FROM maintenance_types mt
              WHERE mt.tenant_id = t.id AND mt.code = v.code
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_MaintenanceEntries_maintenance_types_MaintenanceTypeId",
            table: "MaintenanceEntries");

        migrationBuilder.DropIndex(
            name: "IX_MaintenanceEntries_MaintenanceTypeId",
            table: "MaintenanceEntries");

        migrationBuilder.DropColumn(
            name: "MaintenanceTypeId",
            table: "MaintenanceEntries");

        migrationBuilder.DropTable(
            name: "maintenance_types");
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations;

public class MaintenanceTypesWithCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS maintenance_types (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(100) NOT NULL,
    is_active boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL
);
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_maintenance_types'
    ) THEN
        ALTER TABLE maintenance_types
        ADD CONSTRAINT ""PK_maintenance_types"" PRIMARY KEY (id);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'maintenance_types' AND column_name = 'tenant_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_maintenance_types_tenant_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_maintenance_types_tenant_id"" ON maintenance_types (tenant_id);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'maintenance_types' AND column_name = 'tenant_id'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'maintenance_types' AND column_name = 'code'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_maintenance_types_tenant_id_code' AND relkind = 'i'
    ) THEN
        CREATE UNIQUE INDEX ""IX_maintenance_types_tenant_id_code"" ON maintenance_types (tenant_id, code);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'MaintenanceEntries' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE ""MaintenanceEntries""
            ADD COLUMN IF NOT EXISTS ""MaintenanceTypeId"" uuid NULL;
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'MaintenanceEntries' AND column_name = 'MaintenanceTypeId'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_MaintenanceEntries_MaintenanceTypeId' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_MaintenanceEntries_MaintenanceTypeId""
            ON ""MaintenanceEntries"" (""MaintenanceTypeId"");
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'MaintenanceEntries' AND column_name = 'MaintenanceTypeId'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_MaintenanceEntries_maintenance_types_MaintenanceTypeId'
    ) THEN
        ALTER TABLE ""MaintenanceEntries""
        ADD CONSTRAINT ""FK_MaintenanceEntries_maintenance_types_MaintenanceTypeId""
        FOREIGN KEY (""MaintenanceTypeId"") REFERENCES maintenance_types (id) ON DELETE SET NULL;
    END IF;
END $$;
");

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
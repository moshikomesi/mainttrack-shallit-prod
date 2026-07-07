using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Aligns the maintenance hierarchy with the factory-provided reference map:
    ///   - Adds a generic OVERHAUL component (label "שיפוץ").
    ///   - Adds component mappings for machine.elevatorToDestoner (previously
    ///     had none) and for the 4 water-cooling compressors (previously had
    ///     none).
    ///   - Adds a new top-level "array.conveyors" (מסועים) array with a single
    ///     "machine.conveyors.general" machine and its 5 components. Kept out
    ///     of Morning Round V2 (is_morning_round_enabled = false) since it is
    ///     part of the Maintenance hierarchy only.
    ///   - Assigns the previously-unassigned machine.accumulatorOutside
    ///     (צוברים חוץ) to the Packing House array, per the reference map.
    ///   - Deactivates machines that are not present in the reference map:
    ///     machine.lubrication, machine.chlorineSystem, machine.coolingDoor
    ///     (Rooms) and machine.packing.visionSystem (Packing House).
    /// No existing machine or component definitions are altered.
    /// </summary>
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260707230000_HierarchyMapAlignment")]
    public partial class HierarchyMapAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New generic component: OVERHAUL (שיפוץ)
            migrationBuilder.Sql(
                """
                INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), t.id, 'OVERHAUL', 'maintenanceComponent.overhaul', 25, true, NOW()
                FROM tenants t
                WHERE NOT EXISTS (
                    SELECT 1 FROM machine_components mc WHERE mc.tenant_id = t.id AND mc.code = 'OVERHAUL'
                );
                """);

            // New top-level array: array.conveyors (מסועים), excluded from Morning Round V2
            migrationBuilder.Sql(
                """
                INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
                SELECT gen_random_uuid(), t.id, 'array.conveyors', 8, true, false, NOW()
                FROM tenants t
                WHERE NOT EXISTS (
                    SELECT 1 FROM arrays a WHERE a.tenant_id = t.id AND a.name_key = 'array.conveyors'
                );
                """);

            // New machine within the Conveyors array
            migrationBuilder.Sql(
                """
                INSERT INTO machines (id, tenant_id, name, code, description, is_active, array_id, created_at)
                SELECT gen_random_uuid(), t.id, 'machine.conveyors.general', 'conveyors.general', NULL, true, a.id, NOW()
                FROM tenants t
                JOIN arrays a ON a.tenant_id = t.id AND a.name_key = 'array.conveyors'
                WHERE NOT EXISTS (
                    SELECT 1 FROM machines m WHERE m.tenant_id = t.id AND m.name = 'machine.conveyors.general'
                );
                """);

            // Component mappings: new Conveyors machine, Elevator to Destoner, Water Cooling compressors
            migrationBuilder.Sql(
                """
                INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
                FROM (VALUES
                    ('machine.conveyors.general',          'MOTOR',        1),
                    ('machine.conveyors.general',          'DRIVE_SHAFT',  2),
                    ('machine.conveyors.general',          'DRIVEN_SHAFT', 3),
                    ('machine.conveyors.general',          'CONVEYOR_BELT',4),
                    ('machine.conveyors.general',          'BEARING',      5),
                    ('machine.elevatorToDestoner',         'SHAFT',        1),
                    ('machine.elevatorToDestoner',         'CONVEYOR',     2),
                    ('machine.elevatorToDestoner',         'VOLTA_BELT',   3),
                    ('machine.elevatorToDestoner',         'MOTOR',        4),
                    ('machine.waterCooling.compressor1',   'OVERHAUL',     1),
                    ('machine.waterCooling.compressor1',   'LUBRICATION',  2),
                    ('machine.waterCooling.compressor2',   'OVERHAUL',     1),
                    ('machine.waterCooling.compressor2',   'LUBRICATION',  2),
                    ('machine.waterCooling.compressor3',   'OVERHAUL',     1),
                    ('machine.waterCooling.compressor3',   'LUBRICATION',  2),
                    ('machine.waterCooling.compressor4',   'OVERHAUL',     1),
                    ('machine.waterCooling.compressor4',   'LUBRICATION',  2)
                ) AS spec(machine_name, component_code, sort_order)
                JOIN machines m
                    ON m.name = spec.machine_name
                   AND m.is_active = true
                JOIN machine_components mc
                    ON mc.tenant_id = m.tenant_id
                   AND mc.code = spec.component_code
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM machine_component_mappings existing
                    WHERE existing.machine_id = m.id
                      AND existing.component_id = mc.id
                );
                """);

            // Assign the previously-unassigned Accumulator Outside machine to Packing House
            migrationBuilder.Sql(
                """
                UPDATE machines m
                SET array_id = a.id
                FROM arrays a
                WHERE m.tenant_id = a.tenant_id
                  AND a.name_key = 'array.packing_house'
                  AND m.name = 'machine.accumulatorOutside'
                  AND m.is_active = true
                  AND m.array_id IS NULL;
                """);

            // Deactivate machines not present in the reference hierarchy map
            migrationBuilder.Sql(
                """
                UPDATE machines
                SET is_active = false, array_id = NULL
                WHERE name IN (
                    'machine.lubrication',
                    'machine.chlorineSystem',
                    'machine.coolingDoor',
                    'machine.packing.visionSystem'
                )
                AND is_active = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reactivate previously-deactivated machines and restore their arrays
            migrationBuilder.Sql(
                """
                UPDATE machines m
                SET is_active = true,
                    array_id = a.id
                FROM arrays a
                WHERE a.tenant_id = m.tenant_id
                  AND a.name_key = 'array.rooms'
                  AND m.name IN ('machine.lubrication', 'machine.chlorineSystem', 'machine.coolingDoor');
                """);

            migrationBuilder.Sql(
                """
                UPDATE machines m
                SET is_active = true,
                    array_id = a.id
                FROM arrays a
                WHERE a.tenant_id = m.tenant_id
                  AND a.name_key = 'array.packing_house'
                  AND m.name = 'machine.packing.visionSystem';
                """);

            // Revert Accumulator Outside back to unassigned
            migrationBuilder.Sql(
                """
                UPDATE machines
                SET array_id = NULL
                WHERE name = 'machine.accumulatorOutside';
                """);

            // Remove added component mappings
            migrationBuilder.Sql(
                """
                DELETE FROM machine_component_mappings mcm
                USING machines m
                WHERE mcm.machine_id = m.id
                  AND m.name IN (
                    'machine.conveyors.general',
                    'machine.elevatorToDestoner',
                    'machine.waterCooling.compressor1',
                    'machine.waterCooling.compressor2',
                    'machine.waterCooling.compressor3',
                    'machine.waterCooling.compressor4'
                  );
                """);

            // Remove the new Conveyors machine and array
            migrationBuilder.Sql("DELETE FROM machines WHERE name = 'machine.conveyors.general';");
            migrationBuilder.Sql("DELETE FROM arrays WHERE name_key = 'array.conveyors';");

            // Remove the new OVERHAUL component
            migrationBuilder.Sql("DELETE FROM machine_components WHERE code = 'OVERHAUL';");
        }
    }
}

using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Machine.onion.mixer (מקנבת) component list update:
    ///   - Replaces its RUBBER component with a dedicated RUBBER_STARS entry
    ///     (label "גומיות/כוכבים") so the shared RUBBER catalog entry used by
    ///     other machines (e.g. machine.dryCleaningProcess) is left untouched.
    ///   - Adds a new BEARINGS component (label "מיסבים") to its component
    ///     list. Kept distinct from the existing shared BEARING entry (label
    ///     "מיסב") used by other machines, to avoid any cross-machine impact.
    /// No other machine's component list is affected.
    /// </summary>
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260707220000_UpdateOnionMixerComponents")]
    public partial class UpdateOnionMixerComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
                SELECT v.id, t.id, v.code, v.name_key, v.sort_order, true, NOW()
                FROM tenants t
                CROSS JOIN (VALUES
                    ('c3000001-0000-0000-0000-000000000023'::uuid, 'RUBBER_STARS', 'maintenanceComponent.rubberStars', 23),
                    ('c3000001-0000-0000-0000-000000000024'::uuid, 'BEARINGS',     'maintenanceComponent.bearings',    24)
                ) AS v(id, code, name_key, sort_order)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM machine_components mc
                    WHERE mc.tenant_id = t.id AND mc.code = v.code
                );
                """);

            // Deactivate the shared RUBBER mapping specifically for machine.onion.mixer
            // (the RUBBER catalog entry itself, and its use by other machines, is untouched).
            migrationBuilder.Sql(
                """
                UPDATE machine_component_mappings mcm
                SET is_active = false
                FROM machines m, machine_components mc
                WHERE mcm.machine_id = m.id
                  AND mcm.component_id = mc.id
                  AND mcm.is_active = true
                  AND m.name = 'machine.onion.mixer'
                  AND m.is_active = true
                  AND mc.code = 'RUBBER';
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
                FROM (VALUES
                    ('machine.onion.mixer', 'RUBBER_STARS', 1),
                    ('machine.onion.mixer', 'BEARINGS',     4)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM machine_component_mappings mcm
                USING machines m, machine_components mc
                WHERE mcm.machine_id = m.id
                  AND mcm.component_id = mc.id
                  AND m.name = 'machine.onion.mixer'
                  AND mc.code IN ('RUBBER_STARS', 'BEARINGS');
                """);

            migrationBuilder.Sql(
                """
                UPDATE machine_component_mappings mcm
                SET is_active = true
                FROM machines m, machine_components mc
                WHERE mcm.machine_id = m.id
                  AND mcm.component_id = mc.id
                  AND m.name = 'machine.onion.mixer'
                  AND mc.code = 'RUBBER';
                """);

            migrationBuilder.Sql("DELETE FROM machine_components WHERE code IN ('RUBBER_STARS', 'BEARINGS');");
        }
    }
}

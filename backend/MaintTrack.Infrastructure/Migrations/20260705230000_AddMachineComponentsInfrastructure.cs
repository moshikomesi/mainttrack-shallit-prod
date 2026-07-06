using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260705230000_AddMachineComponentsInfrastructure")]
    public partial class AddMachineComponentsInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS machine_components (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    code character varying(100) NOT NULL,
                    name_key character varying(200) NOT NULL,
                    sort_order integer NOT NULL DEFAULT 0,
                    is_active boolean NOT NULL DEFAULT true,
                    created_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_machine_components PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class
                        WHERE relname = 'ix_machine_components_tenant_id_code'
                          AND relkind = 'i'
                    ) THEN
                        CREATE UNIQUE INDEX ix_machine_components_tenant_id_code
                            ON machine_components (tenant_id, code);
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS machine_component_mappings (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    machine_id uuid NOT NULL,
                    component_id uuid NOT NULL,
                    sort_order integer NOT NULL DEFAULT 0,
                    is_active boolean NOT NULL DEFAULT true,
                    created_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_machine_component_mappings PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class
                        WHERE relname = 'ix_machine_component_mappings_machine_id_component_id'
                          AND relkind = 'i'
                    ) THEN
                        CREATE UNIQUE INDEX ix_machine_component_mappings_machine_id_component_id
                            ON machine_component_mappings (machine_id, component_id);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class
                        WHERE relname = 'ix_machine_component_mappings_tenant_id_machine_id'
                          AND relkind = 'i'
                    ) THEN
                        CREATE INDEX ix_machine_component_mappings_tenant_id_machine_id
                            ON machine_component_mappings (tenant_id, machine_id);
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_machine_component_mappings_machines_machine_id'
                    ) THEN
                        ALTER TABLE machine_component_mappings
                            ADD CONSTRAINT fk_machine_component_mappings_machines_machine_id
                            FOREIGN KEY (machine_id) REFERENCES machines (id) ON DELETE CASCADE;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_machine_component_mappings_machine_components_component_id'
                    ) THEN
                        ALTER TABLE machine_component_mappings
                            ADD CONSTRAINT fk_machine_component_mappings_machine_components_component_id
                            FOREIGN KEY (component_id) REFERENCES machine_components (id) ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
                SELECT v.id, t.id, v.code, v.name_key, v.sort_order, true, NOW()
                FROM tenants t
                CROSS JOIN (VALUES
                    ('c3000001-0000-0000-0000-000000000001'::uuid, 'MOTOR',          'maintenanceComponent.motor',          1),
                    ('c3000001-0000-0000-0000-000000000002'::uuid, 'SHAFT',          'maintenanceComponent.shaft',          2),
                    ('c3000001-0000-0000-0000-000000000003'::uuid, 'BEARING',        'maintenanceComponent.bearing',        3),
                    ('c3000001-0000-0000-0000-000000000004'::uuid, 'BELT',           'maintenanceComponent.belt',           4),
                    ('c3000001-0000-0000-0000-000000000005'::uuid, 'CHAIN',          'maintenanceComponent.chain',          5),
                    ('c3000001-0000-0000-0000-000000000006'::uuid, 'LUBRICATION',    'maintenanceComponent.lubrication',    6),
                    ('c3000001-0000-0000-0000-000000000007'::uuid, 'CONVEYOR',       'maintenanceComponent.conveyor',       7),
                    ('c3000001-0000-0000-0000-000000000008'::uuid, 'VOLTA_BELT',     'maintenanceComponent.voltaBelt',      8),
                    ('c3000001-0000-0000-0000-000000000009'::uuid, 'BRUSH',          'maintenanceComponent.brush',          9),
                    ('c3000001-0000-0000-0000-000000000010'::uuid, 'COUPLING',       'maintenanceComponent.coupling',       10),
                    ('c3000001-0000-0000-0000-000000000011'::uuid, 'PULLEY',         'maintenanceComponent.pulley',         11),
                    ('c3000001-0000-0000-0000-000000000012'::uuid, 'RUBBER',         'maintenanceComponent.rubber',         12),
                    ('c3000001-0000-0000-0000-000000000013'::uuid, 'SUPPORT',        'maintenanceComponent.support',        13),
                    ('c3000001-0000-0000-0000-000000000014'::uuid, 'GUIDE',          'maintenanceComponent.guide',          14),
                    ('c3000001-0000-0000-0000-000000000015'::uuid, 'IMPELLER',       'maintenanceComponent.impeller',       15),
                    ('c3000001-0000-0000-0000-000000000016'::uuid, 'COIL',           'maintenanceComponent.coil',           16),
                    ('c3000001-0000-0000-0000-000000000017'::uuid, 'SCALE',          'maintenanceComponent.scale',          17),
                    ('c3000001-0000-0000-0000-000000000018'::uuid, 'LEAK_OVERHAUL',  'maintenanceComponent.leakOverhaul', 18),
                    ('c3000001-0000-0000-0000-000000000019'::uuid, 'DRIVE_SHAFT',    'maintenanceComponent.driveShaft',     19),
                    ('c3000001-0000-0000-0000-000000000020'::uuid, 'DRIVEN_SHAFT',   'maintenanceComponent.drivenShaft',    20),
                    ('c3000001-0000-0000-0000-000000000021'::uuid, 'CONVEYOR_BELT',  'maintenanceComponent.conveyorBelt',   21),
                    ('c3000001-0000-0000-0000-000000000022'::uuid, 'OTHER',          'maintenanceComponent.other',          22)
                ) AS v(id, code, name_key, sort_order)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM machine_components mc
                    WHERE mc.tenant_id = t.id AND mc.code = v.code
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
                FROM (VALUES
                    -- Washing Array
                    ('machine.smallHopper',            'SCALE',           1),
                    ('machine.smallHopper',            'LUBRICATION',     2),
                    ('machine.smallHopper',            'CHAIN',           3),
                    ('machine.smallHopper',            'BEARING',         4),
                    ('machine.smallHopper',            'BELT',            5),
                    ('machine.largeHopper',            'SCALE',           1),
                    ('machine.largeHopper',            'LUBRICATION',     2),
                    ('machine.largeHopper',            'CHAIN',           3),
                    ('machine.largeHopper',            'BEARING',         4),
                    ('machine.largeHopper',            'BELT',            5),
                    ('machine.dryCleaningProcess',     'MOTOR',           1),
                    ('machine.dryCleaningProcess',     'SHAFT',           2),
                    ('machine.dryCleaningProcess',     'RUBBER',          3),
                    ('machine.destoner',               'SHAFT',           1),
                    ('machine.destoner',               'CONVEYOR',        2),
                    ('machine.destoner',               'MOTOR',           3),
                    ('machine.soakingTank',            'SHAFT',           1),
                    ('machine.soakingTank',            'BELT',            2),
                    ('machine.soakingTank',            'CONVEYOR',        3),
                    ('machine.soakingTank',            'MOTOR',           4),
                    ('machine.soakingPoolPump',        'LUBRICATION',     1),
                    ('machine.internalWashDrum',       'BELT',            1),
                    ('machine.internalWashDrum',       'SHAFT',           2),
                    ('machine.internalWashDrum',       'SUPPORT',         3),
                    ('machine.internalWashDrum',       'MOTOR',           4),
                    ('machine.washingDrumPump',        'LUBRICATION',     1),
                    ('machine.wearBroken2',            'SHAFT',           1),
                    ('machine.wearBroken2',            'MOTOR',           2),
                    ('machine.wearBroken2',            'BELT',            3),
                    ('machine.polisher1',              'BRUSH',           1),
                    ('machine.polisher1',              'SHAFT',           2),
                    ('machine.polisher1',              'COUPLING',        3),
                    ('machine.polisher1',              'BELT',            4),
                    ('machine.polisher1',              'PULLEY',          5),
                    ('machine.polisher1',              'MOTOR',           6),
                    ('machine.polisher2',              'BRUSH',           1),
                    ('machine.polisher2',              'SHAFT',           2),
                    ('machine.polisher2',              'COUPLING',        3),
                    ('machine.polisher2',              'BELT',            4),
                    ('machine.polisher2',              'PULLEY',          5),
                    ('machine.polisher2',              'MOTOR',           6),
                    ('machine.polisher3',              'BRUSH',           1),
                    ('machine.polisher3',              'SHAFT',           2),
                    ('machine.polisher3',              'COUPLING',        3),
                    ('machine.polisher3',              'BELT',            4),
                    ('machine.polisher3',              'PULLEY',          5),
                    ('machine.polisher3',              'MOTOR',           6),
                    ('machine.organicDrum',            'BELT',            1),
                    ('machine.organicDrum',            'MOTOR',           2),
                    ('machine.organicDrum',            'GUIDE',           3),
                    ('machine.roundPitPump',           'IMPELLER',        1),
                    ('machine.roundPitPump',           'COIL',            2),
                    ('machine.organicDrumAbovePool',   'BELT',            1),
                    ('machine.organicDrumAbovePool',   'MOTOR',           2),
                    ('machine.organicDrumAbovePool',   'GUIDE',           3),
                    ('machine.pushPumps2',               'LEAK_OVERHAUL',   1),
                    ('machine.washing.conveyors',      'DRIVE_SHAFT',     1),
                    ('machine.washing.conveyors',      'DRIVEN_SHAFT',    2),
                    ('machine.washing.conveyors',      'CONVEYOR_BELT',   3),
                    -- Onion Array
                    ('machine.onion.mixer',            'RUBBER',          1),
                    ('machine.onion.mixer',            'SHAFT',           2),
                    ('machine.onion.mixer',            'MOTOR',           3),
                    -- Water Cooling Array
                    ('machine.waterCooling.waterPump1', 'IMPELLER',       1),
                    ('machine.waterCooling.waterPump1', 'COIL',           2),
                    ('machine.waterCooling.waterPump1', 'MOTOR',          3),
                    ('machine.waterCooling.waterPump2', 'IMPELLER',       1),
                    ('machine.waterCooling.waterPump2', 'COIL',           2),
                    ('machine.waterCooling.waterPump2', 'MOTOR',          3),
                    ('machine.waterCooling.waterPump3', 'IMPELLER',       1),
                    ('machine.waterCooling.waterPump3', 'COIL',           2),
                    ('machine.waterCooling.waterPump3', 'MOTOR',          3),
                    -- Gan Shmuel (Ginoshar)
                    ('machine.ginoshar.netPackingMachine', 'CONVEYOR',     1),
                    ('machine.ginoshar.netPackingMachine', 'MOTOR',        2),
                    ('machine.ginoshar.netPackingMachine', 'DRIVE_SHAFT',    3),
                    ('machine.ginoshar.netPackingMachine', 'DRIVEN_SHAFT',   4),
                    ('machine.ginoshar.netPackingMachine', 'CONVEYOR_BELT',  5),
                    ('machine.ginoshar.netPackingMachine', 'BEARING',        6)
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
            migrationBuilder.Sql("DROP TABLE IF EXISTS machine_component_mappings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS machine_components;");
        }
    }
}

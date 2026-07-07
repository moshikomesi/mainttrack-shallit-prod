using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a `sort_order` column to `machines` so the machine list within each
    /// array can follow a defined order (previously machines were always
    /// ordered alphabetically by their translation key, which did not match
    /// the factory's reference hierarchy map). Populates it per-array to match
    /// that reference map. Machines not covered by the map (e.g. legacy /
    /// unassigned machines) keep the default value of 0 and sort alphabetically
    /// relative to each other.
    /// </summary>
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260707233000_AddMachineSortOrder")]
    public partial class AddMachineSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'machines'
                          AND column_name = 'sort_order'
                    ) THEN
                        ALTER TABLE machines ADD COLUMN sort_order integer NOT NULL DEFAULT 0;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                UPDATE machines m
                SET sort_order = spec.sort_order
                FROM (VALUES
                    -- Washing Array (מערך שטיפה)
                    ('machine.largeHopper',                1),
                    ('machine.smallHopper',                2),
                    ('machine.dryCleaningProcess',         3),
                    ('machine.elevatorToDestoner',         4),
                    ('machine.destoner',                   5),
                    ('machine.soakingTank',                6),
                    ('machine.soakingPoolPump',            7),
                    ('machine.internalWashDrum',           8),
                    ('machine.washingDrumPump',            9),
                    ('machine.wearBroken2',                10),
                    ('machine.polisher1',                  11),
                    ('machine.polisher2',                  12),
                    ('machine.polisher3',                  13),
                    ('machine.organicDrum',                14),
                    ('machine.roundPitPump',                15),
                    ('machine.organicDrumAbovePool',       16),
                    ('machine.pushPumps2',                 17),
                    ('machine.washing.conveyors',          18),
                    -- Onion Array (מערך בצל)
                    ('machine.onion.hopper',                1),
                    ('machine.onion.elevator',              2),
                    ('machine.onion.washingDrum',           3),
                    ('machine.onion.mixer',                 4),
                    ('machine.onion.sorter',                5),
                    ('machine.onion.conveyors',             6),
                    ('machine.onion.tyingMachine',          7),
                    ('machine.onion.dryingRoom',            8),
                    -- Water Cooling Array (מערך קירור מים)
                    ('machine.waterCooling.compressor1',    1),
                    ('machine.waterCooling.compressor2',    2),
                    ('machine.waterCooling.compressor3',    3),
                    ('machine.waterCooling.compressor4',    4),
                    ('machine.waterCooling.condensers',     5),
                    ('machine.waterCooling.coolingPlate',   6),
                    ('machine.waterCooling.fans',           7),
                    ('machine.waterCooling.waterPump1',     8),
                    ('machine.waterCooling.waterPump2',     9),
                    ('machine.waterCooling.waterPump3',     10),
                    ('machine.waterCooling.automaticWaterFilling', 11),
                    -- Rooms (חדרים)
                    ('machine.rooms.compressor1',           1),
                    ('machine.rooms.compressor2',           2),
                    ('machine.rooms.compressor3',           3),
                    ('machine.rooms.compressor4',           4),
                    ('machine.rooms.roomDoors',             5),
                    ('machine.rooms.fastDoor',               6),
                    ('machine.rooms.condensers',            7),
                    -- Ginoshar (גן שומרון)
                    ('machine.ginoshar.netPackingMachine',   1),
                    ('machine.ginoshar.sensorPackingMachine',2),
                    ('machine.ginoshar.airCompressor',       3),
                    -- Packing House (בית אריזה)
                    ('machine.sorter1',                      1),
                    ('machine.sorter2',                      2),
                    ('machine.sorter3',                      3),
                    ('machine.packing.poolElevators',        4),
                    ('machine.packing.conveyorSystem',       5),
                    ('machine.packing.fastVerbrocken',       6),
                    ('machine.packing.verbrocken',           7),
                    ('machine.packing.bulkFiller',           8),
                    ('machine.packing.metalDetector',        9),
                    ('machine.scale1',                       10),
                    ('machine.packing1',                     11),
                    ('machine.scale2',                       12),
                    ('machine.packing2',                     13),
                    ('machine.scale3',                       14),
                    ('machine.packing3',                     15),
                    ('machine.scale4',                       16),
                    ('machine.packing4',                     17),
                    ('machine.scale5',                       18),
                    ('machine.packing5a',                    19),
                    ('machine.packing5b',                    20),
                    ('machine.sorterLine5',                  21),
                    ('machine.scale6',                       22),
                    ('machine.packing6a',                    23),
                    ('machine.packing6b',                    24),
                    ('machine.sorterLine6',                  25),
                    ('machine.masters',                      26),
                    ('machine.accumulatorOutside',           27),
                    ('machine.viscose',                      28),
                    -- Not in the reference map; kept active, placed at the end
                    ('machine.packing.stackers',             29),
                    ('machine.packing.externalBuckets',      30),
                    -- Conveyors (מסועים)
                    ('machine.conveyors.general',            1)
                ) AS spec(machine_name, sort_order)
                WHERE m.name = spec.machine_name;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'machines'
                          AND column_name = 'sort_order'
                    ) THEN
                        ALTER TABLE machines DROP COLUMN sort_order;
                    END IF;
                END $$;
                """);
        }
    }
}

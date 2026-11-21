using System;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the independent Parameter Photos catalog and links existing photos
    /// to it without rewriting image files.
    ///
    /// Preservation:
    /// - machine_parameter_photos rows and image_url values are kept.
    /// - machine_id remains populated for photos that already had one.
    /// - parameter_id is set only when exactly one machine in the tenant has
    ///   machines.name equal to the parameter name_key. Duplicate names and
    ///   sort-order collisions stay unlinked.
    /// - machine.largeHopper, machine.smallHopper, and machine.onion.hopper
    ///   are never mapped. The standalone Hopper parameter is never an
    ///   automatic link target.
    /// - Down refuses to continue when any photo still has parameter_id set.
    ///   It does not delete photo rows or image files.
    ///
    /// The seed is idempotent and runs for every tenant that exists when this
    /// migration is applied. A tenant created later receives the same catalog
    /// from ParameterPhotoCatalogSeeder on the first catalog read.
    /// This migration does not delete photo files or shared hierarchy rows.
    /// </summary>
    public partial class AddIndependentParameterPhotoCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_machine_parameter_photos_machine_id_sort_order",
                table: "machine_parameter_photos");

            migrationBuilder.AlterColumn<Guid>(
                name: "machine_id",
                table: "machine_parameter_photos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "parameter_id",
                table: "machine_parameter_photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parameter_photo_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_photo_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parameter_photo_parameters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_photo_parameters", x => x.id);
                    table.ForeignKey(
                        name: "FK_parameter_photo_parameters_parameter_photo_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "parameter_photo_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_machine_id_sort_order",
                table: "machine_parameter_photos",
                columns: new[] { "machine_id", "sort_order" },
                unique: true,
                filter: "\"machine_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_parameter_id_sort_order",
                table: "machine_parameter_photos",
                columns: new[] { "parameter_id", "sort_order" },
                unique: true,
                filter: "\"parameter_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_tenant_id_parameter_id",
                table: "machine_parameter_photos",
                columns: new[] { "tenant_id", "parameter_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_parameter_photos_association",
                table: "machine_parameter_photos",
                sql: "parameter_id IS NOT NULL OR machine_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_groups_tenant_id_name_key",
                table: "parameter_photo_groups",
                columns: new[] { "tenant_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_parameters_group_id",
                table: "parameter_photo_parameters",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_parameters_tenant_id_name_key",
                table: "parameter_photo_parameters",
                columns: new[] { "tenant_id", "name_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos",
                column: "parameter_id",
                principalTable: "parameter_photo_parameters",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                INSERT INTO parameter_photo_groups (id, tenant_id, name_key, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), tenant.id, spec.name_key, spec.sort_order, true, NOW()
                FROM tenants AS tenant
                CROSS JOIN (VALUES
                    ('array.washing_system', 1),
                    ('parameterPhoto.group.coolingSystem', 2),
                    ('array.ginoshar', 3),
                    ('array.packing_house', 4)
                ) AS spec(name_key, sort_order)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM parameter_photo_groups AS existing
                    WHERE existing.tenant_id = tenant.id
                      AND existing.name_key = spec.name_key
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO parameter_photo_parameters (id, tenant_id, group_id, name_key, sort_order, is_active, created_at)
                SELECT gen_random_uuid(), catalog_group.tenant_id, catalog_group.id, spec.name_key, spec.sort_order, true, NOW()
                FROM parameter_photo_groups AS catalog_group
                JOIN (VALUES
                    ('array.washing_system', 'parameterPhoto.parameter.hopper', 1),
                    ('array.washing_system', 'parameterPhoto.parameter.coolingSystemComputer', 2),
                    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.compressorRooms', 1),
                    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.controlRoomAboveCoolingRooms', 2),
                    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.humidityControllers', 3),
                    ('array.ginoshar', 'machine.ginoshar.netPackingMachine', 1),
                    ('array.ginoshar', 'machine.ginoshar.sensorPackingMachine', 2),
                    ('array.ginoshar', 'machine.ginoshar.airCompressor', 3),
                    ('array.ginoshar', 'parameterPhoto.parameter.bagPullersUnderViscose', 4),
                    ('array.packing_house', 'machine.sorter1', 1),
                    ('array.packing_house', 'machine.sorter2', 2),
                    ('array.packing_house', 'machine.sorter3', 3),
                    ('array.packing_house', 'machine.packing.poolElevators', 4),
                    ('array.packing_house', 'machine.packing.conveyorSystem', 5),
                    ('array.packing_house', 'machine.packing.fastVerbrocken', 6),
                    ('array.packing_house', 'machine.packing.verbrocken', 7),
                    ('array.packing_house', 'machine.packing.bulkFiller', 8),
                    ('array.packing_house', 'machine.packing.metalDetector', 9),
                    ('array.packing_house', 'machine.scale1', 10),
                    ('array.packing_house', 'machine.packing1', 11),
                    ('array.packing_house', 'machine.scale2', 12),
                    ('array.packing_house', 'machine.packing2', 13),
                    ('array.packing_house', 'machine.scale3', 14),
                    ('array.packing_house', 'machine.packing3', 15),
                    ('array.packing_house', 'machine.scale4', 16),
                    ('array.packing_house', 'machine.packing4', 17),
                    ('array.packing_house', 'machine.scale5', 18),
                    ('array.packing_house', 'machine.packing5a', 19),
                    ('array.packing_house', 'machine.packing5b', 20),
                    ('array.packing_house', 'machine.sorterLine5', 21),
                    ('array.packing_house', 'machine.scale6', 22),
                    ('array.packing_house', 'machine.packing6a', 23),
                    ('array.packing_house', 'machine.packing6b', 24),
                    ('array.packing_house', 'machine.sorterLine6', 25),
                    ('array.packing_house', 'machine.masters', 26),
                    ('array.packing_house', 'machine.accumulatorOutside', 27),
                    ('array.packing_house', 'machine.viscose', 28)
                ) AS spec(group_key, name_key, sort_order)
                    ON catalog_group.name_key = spec.group_key
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM parameter_photo_parameters AS existing
                    WHERE existing.tenant_id = catalog_group.tenant_id
                      AND existing.name_key = spec.name_key
                );
                """);

            migrationBuilder.Sql(ParameterPhotoLegacyLinker.MigrationLinkSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ParameterPhotoRollbackGuard.Sql);

            migrationBuilder.DropForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos");

            migrationBuilder.DropTable(
                name: "parameter_photo_parameters");

            migrationBuilder.DropTable(
                name: "parameter_photo_groups");

            migrationBuilder.DropIndex(
                name: "IX_machine_parameter_photos_machine_id_sort_order",
                table: "machine_parameter_photos");

            migrationBuilder.DropIndex(
                name: "IX_machine_parameter_photos_parameter_id_sort_order",
                table: "machine_parameter_photos");

            migrationBuilder.DropIndex(
                name: "IX_machine_parameter_photos_tenant_id_parameter_id",
                table: "machine_parameter_photos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_machine_parameter_photos_association",
                table: "machine_parameter_photos");

            migrationBuilder.DropColumn(
                name: "parameter_id",
                table: "machine_parameter_photos");

            migrationBuilder.AlterColumn<Guid>(
                name: "machine_id",
                table: "machine_parameter_photos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_machine_id_sort_order",
                table: "machine_parameter_photos",
                columns: new[] { "machine_id", "sort_order" },
                unique: true);
        }
    }
}

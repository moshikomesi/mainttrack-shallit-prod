using System;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterPhotoReviewRemediation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ParameterPhotoExclusionProjector.TenantIntegrityGuardSql);

            migrationBuilder.DropForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos");

            migrationBuilder.DropForeignKey(
                name: "FK_parameter_photo_parameters_parameter_photo_groups_group_id",
                table: "parameter_photo_parameters");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_parameter_photo_parameters_id_tenant_id",
                table: "parameter_photo_parameters",
                columns: new[] { "id", "tenant_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_parameter_photo_groups_id_tenant_id",
                table: "parameter_photo_groups",
                columns: new[] { "id", "tenant_id" });

            migrationBuilder.CreateTable(
                name: "parameter_photo_parameter_exclusions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_photo_parameter_exclusions", x => x.id);
                    table.ForeignKey(
                        name: "FK_parameter_photo_parameter_exclusions_parameter_photo_parame~",
                        columns: x => new { x.parameter_id, x.tenant_id },
                        principalTable: "parameter_photo_parameters",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_parameters_group_id_tenant_id",
                table: "parameter_photo_parameters",
                columns: new[] { "group_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_parameter_id_tenant_id",
                table: "machine_parameter_photos",
                columns: new[] { "parameter_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_parameter_exclusions_parameter_id_tenant_id",
                table: "parameter_photo_parameter_exclusions",
                columns: new[] { "parameter_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_parameter_photo_parameter_exclusions_tenant_id_parameter_id",
                table: "parameter_photo_parameter_exclusions",
                columns: new[] { "tenant_id", "parameter_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos",
                columns: new[] { "parameter_id", "tenant_id" },
                principalTable: "parameter_photo_parameters",
                principalColumns: new[] { "id", "tenant_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parameter_photo_parameters_parameter_photo_groups_group_id_~",
                table: "parameter_photo_parameters",
                columns: new[] { "group_id", "tenant_id" },
                principalTable: "parameter_photo_groups",
                principalColumns: new[] { "id", "tenant_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(ParameterPhotoExclusionProjector.ProjectSql);
            migrationBuilder.Sql(ParameterPhotoExclusionProjector.UnlinkExcludedLegacyPhotosSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos");

            migrationBuilder.DropForeignKey(
                name: "FK_parameter_photo_parameters_parameter_photo_groups_group_id_~",
                table: "parameter_photo_parameters");

            migrationBuilder.DropTable(
                name: "parameter_photo_parameter_exclusions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_parameter_photo_parameters_id_tenant_id",
                table: "parameter_photo_parameters");

            migrationBuilder.DropIndex(
                name: "IX_parameter_photo_parameters_group_id_tenant_id",
                table: "parameter_photo_parameters");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_parameter_photo_groups_id_tenant_id",
                table: "parameter_photo_groups");

            migrationBuilder.DropIndex(
                name: "IX_machine_parameter_photos_parameter_id_tenant_id",
                table: "machine_parameter_photos");

            migrationBuilder.AddForeignKey(
                name: "FK_machine_parameter_photos_parameter_photo_parameters_paramet~",
                table: "machine_parameter_photos",
                column: "parameter_id",
                principalTable: "parameter_photo_parameters",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parameter_photo_parameters_parameter_photo_groups_group_id",
                table: "parameter_photo_parameters",
                column: "group_id",
                principalTable: "parameter_photo_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

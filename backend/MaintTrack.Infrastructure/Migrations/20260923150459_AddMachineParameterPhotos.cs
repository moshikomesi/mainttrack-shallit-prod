using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260923150459_AddMachineParameterPhotos")]
    public partial class AddMachineParameterPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "array_feature_visibility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    array_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "text", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_array_feature_visibility", x => x.id);
                    table.ForeignKey(
                        name: "FK_array_feature_visibility_arrays_array_id",
                        column: x => x.array_id,
                        principalTable: "arrays",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "machine_parameter_photo_managers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_parameter_photo_managers", x => x.id);
                    table.ForeignKey(
                        name: "FK_machine_parameter_photo_managers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "machine_parameter_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_parameter_photos", x => x.id);
                    table.ForeignKey(
                        name: "FK_machine_parameter_photos_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_array_feature_visibility_array_id",
                table: "array_feature_visibility",
                column: "array_id");

            migrationBuilder.CreateIndex(
                name: "IX_array_feature_visibility_tenant_id_array_id_feature_key",
                table: "array_feature_visibility",
                columns: new[] { "tenant_id", "array_id", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photo_managers_tenant_id_user_id",
                table: "machine_parameter_photo_managers",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photo_managers_user_id",
                table: "machine_parameter_photo_managers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_machine_id_sort_order",
                table: "machine_parameter_photos",
                columns: new[] { "machine_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_photos_tenant_id_machine_id",
                table: "machine_parameter_photos",
                columns: new[] { "tenant_id", "machine_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "array_feature_visibility");

            migrationBuilder.DropTable(
                name: "machine_parameter_photo_managers");

            migrationBuilder.DropTable(
                name: "machine_parameter_photos");
        }
    }
}

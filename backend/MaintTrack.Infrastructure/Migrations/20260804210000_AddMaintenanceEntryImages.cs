using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260804210000_AddMaintenanceEntryImages")]
    public partial class AddMaintenanceEntryImages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_entry_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_entry_images", x => x.id);
                    table.CheckConstraint(
                        "ck_maintenance_entry_images_sort_order",
                        "sort_order >= 1 AND sort_order <= 2");
                    table.ForeignKey(
                        name: "FK_maintenance_entry_images_MaintenanceEntries_maintenance_entry_id",
                        column: x => x.maintenance_entry_id,
                        principalTable: "MaintenanceEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_entry_images_maintenance_entry_id_sort_order",
                table: "maintenance_entry_images",
                columns: new[] { "maintenance_entry_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_entry_images_tenant_id_maintenance_entry_id",
                table: "maintenance_entry_images",
                columns: new[] { "tenant_id", "maintenance_entry_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "maintenance_entry_images");
        }
    }
}

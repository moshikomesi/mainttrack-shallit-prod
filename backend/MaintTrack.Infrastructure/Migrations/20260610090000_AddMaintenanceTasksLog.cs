using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260610090000_AddMaintenanceTasksLog")]
    public partial class AddMaintenanceTasksLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_annual_plan_items_maintenance_tasks_task_id",
                table: "annual_plan_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_maintenance_tasks",
                table: "maintenance_tasks");

            migrationBuilder.RenameTable(
                name: "maintenance_tasks",
                newName: "annual_plan_tasks");

            migrationBuilder.RenameIndex(
                name: "IX_maintenance_tasks_tenant_id_type_order_index",
                table: "annual_plan_tasks",
                newName: "IX_annual_plan_tasks_tenant_id_type_order_index");

            migrationBuilder.AddPrimaryKey(
                name: "PK_annual_plan_tasks",
                table: "annual_plan_tasks",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_annual_plan_items_annual_plan_tasks_task_id",
                table: "annual_plan_items",
                column: "task_id",
                principalTable: "annual_plan_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "maintenance_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_tenant_id_created_at",
                table: "maintenance_tasks",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_tenant_id_task_date",
                table: "maintenance_tasks",
                columns: new[] { "tenant_id", "task_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_annual_plan_items_annual_plan_tasks_task_id",
                table: "annual_plan_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_annual_plan_tasks",
                table: "annual_plan_tasks");

            migrationBuilder.RenameTable(
                name: "annual_plan_tasks",
                newName: "maintenance_tasks");

            migrationBuilder.RenameIndex(
                name: "IX_annual_plan_tasks_tenant_id_type_order_index",
                table: "maintenance_tasks",
                newName: "IX_maintenance_tasks_tenant_id_type_order_index");

            migrationBuilder.AddPrimaryKey(
                name: "PK_maintenance_tasks",
                table: "maintenance_tasks",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_annual_plan_items_maintenance_tasks_task_id",
                table: "annual_plan_items",
                column: "task_id",
                principalTable: "maintenance_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

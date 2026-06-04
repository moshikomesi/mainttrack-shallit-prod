using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "annual_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    translation_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "technicians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technicians", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "annual_plan_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plan_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_annual_plan_items_annual_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "annual_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_plan_items_maintenance_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "maintenance_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "annual_plan_dates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plan_dates", x => x.id);
                    table.ForeignKey(
                        name: "FK_annual_plan_dates_annual_plan_items_plan_item_id",
                        column: x => x.plan_item_id,
                        principalTable: "annual_plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "annual_plan_execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    required_days = table.Column<int>(type: "integer", nullable: true),
                    planned_finish = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_finish = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plan_execution", x => x.id);
                    table.ForeignKey(
                        name: "FK_annual_plan_execution_annual_plan_items_plan_item_id",
                        column: x => x.plan_item_id,
                        principalTable: "annual_plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "annual_plan_workers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plan_workers", x => x.id);
                    table.ForeignKey(
                        name: "FK_annual_plan_workers_annual_plan_execution_execution_id",
                        column: x => x.execution_id,
                        principalTable: "annual_plan_execution",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_plan_workers_technicians_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technicians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_dates_plan_item_id",
                table: "annual_plan_dates",
                column: "plan_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_execution_plan_item_id",
                table: "annual_plan_execution",
                column: "plan_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_items_plan_id",
                table: "annual_plan_items",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_items_task_id",
                table: "annual_plan_items",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_workers_execution_id",
                table: "annual_plan_workers",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_workers_technician_id",
                table: "annual_plan_workers",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plans_tenant_id_year_type",
                table: "annual_plans",
                columns: new[] { "tenant_id", "year", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_tenant_id_type_order_index",
                table: "maintenance_tasks",
                columns: new[] { "tenant_id", "type", "order_index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annual_plan_dates");

            migrationBuilder.DropTable(
                name: "annual_plan_workers");

            migrationBuilder.DropTable(
                name: "annual_plan_execution");

            migrationBuilder.DropTable(
                name: "technicians");

            migrationBuilder.DropTable(
                name: "annual_plan_items");

            migrationBuilder.DropTable(
                name: "annual_plans");

            migrationBuilder.DropTable(
                name: "maintenance_tasks");
        }
    }
}

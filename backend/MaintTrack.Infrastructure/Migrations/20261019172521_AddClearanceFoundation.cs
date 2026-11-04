using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20261019172521_AddClearanceFoundation")]
    public partial class AddClearanceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clearance_approvers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clearance_approvers", x => x.id);
                    table.ForeignKey(
                        name: "FK_clearance_approvers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clearance_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    maintenance_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    morning_round_v2_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clearance_items", x => x.id);
                    table.CheckConstraint("ck_clearance_items_decision", "(status = 'pending' AND decided_by_user_id IS NULL AND decided_at IS NULL) OR (status IN ('approved', 'rejected') AND decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL)");
                    table.CheckConstraint("ck_clearance_items_source_reference", "(source_type = 'maintenance_entry' AND maintenance_entry_id IS NOT NULL AND morning_round_v2_submission_id IS NULL) OR (source_type = 'morning_round_v2' AND maintenance_entry_id IS NULL AND morning_round_v2_submission_id IS NOT NULL)");
                    table.CheckConstraint("ck_clearance_items_source_type", "source_type IN ('maintenance_entry', 'morning_round_v2')");
                    table.CheckConstraint("ck_clearance_items_status", "status IN ('pending', 'approved', 'rejected')");
                    table.ForeignKey(
                        name: "FK_clearance_items_MaintenanceEntries_maintenance_entry_id",
                        column: x => x.maintenance_entry_id,
                        principalTable: "MaintenanceEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clearance_items_morning_round_v2_submissions_submission_id",
                        column: x => x.morning_round_v2_submission_id,
                        principalTable: "morning_round_v2_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clearance_items_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clearance_approvers_tenant_id_user_id",
                table: "clearance_approvers",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clearance_approvers_user_id",
                table: "clearance_approvers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_items_decided_by_user_id",
                table: "clearance_items",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_items_maintenance_entry_id",
                table: "clearance_items",
                column: "maintenance_entry_id",
                unique: true,
                filter: "maintenance_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_items_morning_round_v2_submission_id",
                table: "clearance_items",
                column: "morning_round_v2_submission_id",
                unique: true,
                filter: "morning_round_v2_submission_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_items_tenant_id_status_submitted_at",
                table: "clearance_items",
                columns: new[] { "tenant_id", "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clearance_approvers");

            migrationBuilder.DropTable(
                name: "clearance_items");
        }
    }
}

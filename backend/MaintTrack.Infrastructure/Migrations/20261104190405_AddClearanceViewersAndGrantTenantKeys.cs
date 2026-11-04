using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20261104190405_AddClearanceViewersAndGrantTenantKeys")]
    public partial class AddClearanceViewersAndGrantTenantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_users_id_tenant_id",
                table: "users",
                columns: new[] { "id", "tenant_id" });

            migrationBuilder.DropForeignKey(
                name: "FK_clearance_approvers_users_user_id",
                table: "clearance_approvers");

            migrationBuilder.DropIndex(
                name: "IX_clearance_approvers_user_id",
                table: "clearance_approvers");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_approvers_user_id_tenant_id",
                table: "clearance_approvers",
                columns: new[] { "user_id", "tenant_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_clearance_approvers_users_user_id_tenant_id",
                table: "clearance_approvers",
                columns: new[] { "user_id", "tenant_id" },
                principalTable: "users",
                principalColumns: new[] { "id", "tenant_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "clearance_viewers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clearance_viewers", x => x.id);
                    table.ForeignKey(
                        name: "FK_clearance_viewers_users_user_id_tenant_id",
                        columns: x => new { x.user_id, x.tenant_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clearance_viewers_tenant_id_user_id",
                table: "clearance_viewers",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clearance_viewers_user_id_tenant_id",
                table: "clearance_viewers",
                columns: new[] { "user_id", "tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clearance_viewers");

            migrationBuilder.DropForeignKey(
                name: "FK_clearance_approvers_users_user_id_tenant_id",
                table: "clearance_approvers");

            migrationBuilder.DropIndex(
                name: "IX_clearance_approvers_user_id_tenant_id",
                table: "clearance_approvers");

            migrationBuilder.CreateIndex(
                name: "IX_clearance_approvers_user_id",
                table: "clearance_approvers",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clearance_approvers_users_user_id",
                table: "clearance_approvers",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropUniqueConstraint(
                name: "AK_users_id_tenant_id",
                table: "users");
        }
    }
}

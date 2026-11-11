using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20261108191918_AddMachineParameterMachineExclusions")]
    public partial class AddMachineParameterMachineExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_parameter_machine_visibility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_parameter_machine_visibility", x => x.id);
                    table.ForeignKey(
                        name: "FK_machine_parameter_machine_visibility_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_machine_visibility_machine_id",
                table: "machine_parameter_machine_visibility",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "IX_machine_parameter_machine_visibility_tenant_id_machine_id",
                table: "machine_parameter_machine_visibility",
                columns: new[] { "tenant_id", "machine_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_parameter_machine_visibility");
        }
    }
}

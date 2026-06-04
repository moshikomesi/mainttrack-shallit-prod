using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations;

public partial class AddRolesAndUserRoleId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_roles_name",
            table: "roles",
            column: "name",
            unique: true);

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "name" },
            values: new object[,]
            {
                { 1, "Worker" },
                { 2, "Manager" },
                { 3, "SuperAdmin" }
            });

        migrationBuilder.AddColumn<int>(
            name: "role_id",
            table: "users",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql(@"
            UPDATE users
            SET role_id = CASE
                WHEN lower(role) = 'manager' THEN 2
                WHEN lower(role) IN ('superadmin', 'super_admin', 'super-admin') THEN 3
                ELSE 1
            END;
        ");

        migrationBuilder.CreateIndex(
            name: "IX_users_role_id",
            table: "users",
            column: "role_id");

        migrationBuilder.AddForeignKey(
            name: "FK_users_roles_role_id",
            table: "users",
            column: "role_id",
            principalTable: "roles",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropColumn(
            name: "role",
            table: "users");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "role",
            table: "users",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "Worker");

        migrationBuilder.Sql(@"
            UPDATE users u
            SET role = COALESCE(r.name, 'Worker')
            FROM roles r
            WHERE u.role_id = r.id;
        ");

        migrationBuilder.DropForeignKey(
            name: "FK_users_roles_role_id",
            table: "users");

        migrationBuilder.DropIndex(
            name: "IX_users_role_id",
            table: "users");

        migrationBuilder.DropColumn(
            name: "role_id",
            table: "users");

        migrationBuilder.DropTable(
            name: "roles");
    }
}


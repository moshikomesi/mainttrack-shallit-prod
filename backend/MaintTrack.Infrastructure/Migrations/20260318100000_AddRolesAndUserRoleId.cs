using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations;

public partial class AddRolesAndUserRoleId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS roles (
    id integer NOT NULL,
    name character varying(50) NOT NULL
);
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_roles'
    ) THEN
        ALTER TABLE roles ADD CONSTRAINT ""PK_roles"" PRIMARY KEY (id);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'roles' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'roles' AND column_name = 'name'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_roles_name' AND relkind = 'i'
    ) THEN
        CREATE UNIQUE INDEX ""IX_roles_name"" ON roles (name);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
INSERT INTO roles (id, name)
VALUES
    (1, 'Worker'),
    (2, 'Manager'),
    (3, 'SuperAdmin')
ON CONFLICT (id) DO NOTHING;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'users' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id integer NOT NULL DEFAULT 1;
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
            UPDATE users
            SET role_id = CASE
                WHEN lower(role) = 'manager' THEN 2
                WHEN lower(role) IN ('superadmin', 'super_admin', 'super-admin') THEN 3
                ELSE 1
            END
            WHERE EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_name = 'users' AND column_name = 'role'
            );
        ");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'role_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_users_role_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_users_role_id"" ON users (role_id);
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'role_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'roles' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_users_roles_role_id'
    ) THEN
        ALTER TABLE users
        ADD CONSTRAINT ""FK_users_roles_role_id""
        FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE RESTRICT;
    END IF;
END $$;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'users' AND column_name = 'role'
    ) THEN
        ALTER TABLE users DROP COLUMN role;
    END IF;
END $$;
");
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


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
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_annual_plan_items_maintenance_tasks_task_id'
    ) THEN
        ALTER TABLE annual_plan_items
        DROP CONSTRAINT ""FK_annual_plan_items_maintenance_tasks_task_id"";
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
DECLARE
    pk_table text;
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_maintenance_tasks'
    ) THEN
        SELECT c.relname
        INTO pk_table
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        WHERE con.conname = 'PK_maintenance_tasks'
        LIMIT 1;

        IF pk_table IS NOT NULL THEN
            EXECUTE format(
                'ALTER TABLE %I DROP CONSTRAINT %I',
                pk_table,
                'PK_maintenance_tasks'
            );
        END IF;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'annual_plan_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    ) THEN
        ALTER TABLE maintenance_tasks RENAME TO annual_plan_tasks;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class
        WHERE relname = 'IX_maintenance_tasks_tenant_id_type_order_index'
          AND relkind = 'i'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM pg_class
        WHERE relname = 'IX_annual_plan_tasks_tenant_id_type_order_index'
          AND relkind = 'i'
    ) THEN
        ALTER INDEX ""IX_maintenance_tasks_tenant_id_type_order_index""
        RENAME TO ""IX_annual_plan_tasks_tenant_id_type_order_index"";
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'annual_plan_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    ) THEN
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS tenant_id uuid;
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS type character varying(50);
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS order_index integer;
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS translation_key character varying(200);
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS created_at timestamp with time zone;
        ALTER TABLE annual_plan_tasks ADD COLUMN IF NOT EXISTS updated_at timestamp with time zone;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_class
            WHERE relname = 'IX_annual_plan_tasks_tenant_id_type_order_index'
              AND relkind = 'i'
        )
        AND EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'annual_plan_tasks'
              AND column_name = 'tenant_id'
        )
        AND EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'annual_plan_tasks'
              AND column_name = 'type'
        )
        AND EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'annual_plan_tasks'
              AND column_name = 'order_index'
        ) THEN
            CREATE INDEX ""IX_annual_plan_tasks_tenant_id_type_order_index""
                ON annual_plan_tasks (tenant_id, type, order_index);
        END IF;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'annual_plan_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    )
    AND EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_maintenance_tasks'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_annual_plan_tasks'
    ) THEN
        ALTER TABLE annual_plan_tasks
        RENAME CONSTRAINT ""PK_maintenance_tasks"" TO ""PK_annual_plan_tasks"";
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'annual_plan_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    )
    AND EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'annual_plan_tasks'
          AND column_name = 'id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_annual_plan_tasks'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_maintenance_tasks'
    ) THEN
        ALTER TABLE annual_plan_tasks
        ADD CONSTRAINT ""PK_annual_plan_tasks"" PRIMARY KEY (id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'annual_plan_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    )
    AND EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'annual_plan_items'
          AND column_name = 'task_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_annual_plan_items_annual_plan_tasks_task_id'
    ) THEN
        ALTER TABLE annual_plan_items
        ADD CONSTRAINT ""FK_annual_plan_items_annual_plan_tasks_task_id""
        FOREIGN KEY (task_id) REFERENCES annual_plan_tasks (id) ON DELETE RESTRICT;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS maintenance_tasks (
    id uuid NOT NULL,
    task_date timestamp with time zone NOT NULL,
    image_url text NOT NULL,
    description text NULL,
    is_confirmed boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL
);
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_maintenance_tasks'
    ) THEN
        ALTER TABLE maintenance_tasks
        ADD CONSTRAINT ""PK_maintenance_tasks"" PRIMARY KEY (id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_tasks'
          AND c.relkind = 'r'
          AND n.nspname = 'public'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = 'IX_maintenance_tasks_tenant_id_created_at'
              AND relkind = 'i'
        )
        AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'maintenance_tasks'
              AND column_name = 'tenant_id'
        )
        AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'maintenance_tasks'
              AND column_name = 'created_at'
        ) THEN
            CREATE INDEX ""IX_maintenance_tasks_tenant_id_created_at""
                ON maintenance_tasks (tenant_id, created_at);
        END IF;

        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = 'IX_maintenance_tasks_tenant_id_task_date'
              AND relkind = 'i'
        )
        AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'maintenance_tasks'
              AND column_name = 'tenant_id'
        )
        AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'maintenance_tasks'
              AND column_name = 'task_date'
        ) THEN
            CREATE INDEX ""IX_maintenance_tasks_tenant_id_task_date""
                ON maintenance_tasks (tenant_id, task_date);
        END IF;
    END IF;
END $$;
");
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

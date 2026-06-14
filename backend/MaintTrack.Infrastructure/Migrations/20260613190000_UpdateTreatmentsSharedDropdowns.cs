using System;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260613190000_UpdateTreatmentsSharedDropdowns")]
    public partial class UpdateTreatmentsSharedDropdowns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'treatments' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS machine_id uuid NULL;
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS maintenance_type_id uuid NULL;
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS created_by_user_id uuid NULL;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'created_by_user_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_created_by_user_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_created_by_user_id"" ON treatments (created_by_user_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_machine_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_machine_id"" ON treatments (machine_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_maintenance_type_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_maintenance_type_id"" ON treatments (maintenance_type_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'tenant_id'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_machine_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_tenant_id_machine_id"" ON treatments (tenant_id, machine_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'tenant_id'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_maintenance_type_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_tenant_id_maintenance_type_id"" ON treatments (tenant_id, maintenance_type_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'machines' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_machines_machine_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT ""FK_treatments_machines_machine_id""
        FOREIGN KEY (machine_id) REFERENCES machines (id) ON DELETE SET NULL;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_maintenance_types_maintenance_type_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT ""FK_treatments_maintenance_types_maintenance_type_id""
        FOREIGN KEY (maintenance_type_id) REFERENCES maintenance_types (id) ON DELETE SET NULL;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'created_by_user_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'users' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_users_created_by_user_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT ""FK_treatments_users_created_by_user_id""
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_treatments_machines_machine_id",
                table: "treatments");

            migrationBuilder.DropForeignKey(
                name: "FK_treatments_maintenance_types_maintenance_type_id",
                table: "treatments");

            migrationBuilder.DropForeignKey(
                name: "FK_treatments_users_created_by_user_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_created_by_user_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_machine_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_maintenance_type_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_tenant_id_machine_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_tenant_id_maintenance_type_id",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "machine_id",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "maintenance_type_id",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "treatments");
        }
    }
}

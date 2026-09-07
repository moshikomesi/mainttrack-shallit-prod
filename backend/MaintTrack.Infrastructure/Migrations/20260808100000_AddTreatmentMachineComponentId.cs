using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260808100000_AddTreatmentMachineComponentId")]
    public partial class AddTreatmentMachineComponentId : Migration
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
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS machine_component_id uuid NULL;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_machine_component_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_machine_component_id"" ON treatments (machine_component_id);
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
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_machine_component_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_treatments_tenant_id_machine_component_id"" ON treatments (tenant_id, machine_component_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'machine_components' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_machine_components_machine_component_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT ""FK_treatments_machine_components_machine_component_id""
        FOREIGN KEY (machine_component_id) REFERENCES machine_components (id) ON DELETE SET NULL;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_treatments_machine_components_machine_component_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_machine_component_id",
                table: "treatments");

            migrationBuilder.DropIndex(
                name: "IX_treatments_tenant_id_machine_component_id",
                table: "treatments");

            migrationBuilder.DropColumn(
                name: "machine_component_id",
                table: "treatments");
        }
    }
}

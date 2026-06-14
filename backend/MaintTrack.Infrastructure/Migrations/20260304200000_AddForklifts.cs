using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForklifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS forklifts (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    license_number character varying(200) NOT NULL,
    description character varying(2000) NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL
);
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'PK_forklifts'
    ) THEN
        ALTER TABLE forklifts ADD CONSTRAINT ""PK_forklifts"" PRIMARY KEY (id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'forklift_reports' AND column_name = 'forklift_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_forklift_reports_forklift_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX ""IX_forklift_reports_forklift_id"" ON forklift_reports (forklift_id);
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'forklift_reports' AND column_name = 'forklift_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'forklifts' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_forklift_reports_forklifts_forklift_id'
    ) THEN
        ALTER TABLE forklift_reports
        ADD CONSTRAINT ""FK_forklift_reports_forklifts_forklift_id""
        FOREIGN KEY (forklift_id) REFERENCES forklifts (id) ON DELETE RESTRICT;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_forklift_reports_forklifts_forklift_id",
                table: "forklift_reports");

            migrationBuilder.DropIndex(
                name: "IX_forklift_reports_forklift_id",
                table: "forklift_reports");

            migrationBuilder.DropTable(
                name: "forklifts");
        }
    }
}

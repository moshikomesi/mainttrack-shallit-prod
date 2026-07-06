using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260613210000_AddArraysAndMachineArrayId")]
    public partial class AddArraysAndMachineArrayId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS arrays (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    name_key text NOT NULL,
                    sort_order integer NOT NULL DEFAULT 0,
                    is_active boolean NOT NULL DEFAULT true,
                    created_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_arrays PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE c.relname = 'machines' AND c.relkind = 'r' AND n.nspname = 'public'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'machines'
                          AND column_name = 'array_id'
                    ) THEN
                        ALTER TABLE machines ADD COLUMN array_id uuid NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'machines'
                          AND column_name = 'array_id'
                    ) THEN
                        ALTER TABLE machines DROP COLUMN array_id;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("DROP TABLE IF EXISTS arrays;");
        }
    }
}

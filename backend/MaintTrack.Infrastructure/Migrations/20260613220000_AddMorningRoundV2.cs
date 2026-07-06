using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260613220000_AddMorningRoundV2")]
    public partial class AddMorningRoundV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE c.relname = 'arrays' AND c.relkind = 'r' AND n.nspname = 'public'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'arrays'
                          AND column_name = 'is_morning_round_enabled'
                    ) THEN
                        ALTER TABLE arrays
                            ADD COLUMN is_morning_round_enabled boolean NOT NULL DEFAULT false;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS morning_round_v2_submissions (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    report_date date NOT NULL,
                    submitted_at timestamp with time zone NOT NULL,
                    submitted_by_user_id uuid NOT NULL,
                    items_json jsonb NOT NULL DEFAULT '[]'::jsonb,
                    created_at timestamp with time zone NOT NULL,
                    updated_at timestamp with time zone NULL,
                    CONSTRAINT pk_morning_round_v2_submissions PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'uq_morning_round_v2_submissions_tenant_report_date'
                    ) THEN
                        ALTER TABLE morning_round_v2_submissions
                            ADD CONSTRAINT uq_morning_round_v2_submissions_tenant_report_date
                            UNIQUE (tenant_id, report_date);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS morning_round_v2_submissions;");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'arrays'
                          AND column_name = 'is_morning_round_enabled'
                    ) THEN
                        ALTER TABLE arrays DROP COLUMN is_morning_round_enabled;
                    END IF;
                END $$;
                """);
        }
    }
}

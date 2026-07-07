using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a small, reusable per-user feature flag mechanism to `users`:
    ///   - enable_new_morning_round
    ///   - enable_new_maintenance_log
    /// Both default to false so any future feature can be rolled out to
    /// selected users first (before a wider release) without branching the
    /// architecture per feature. Frontend navigation reads these flags from
    /// the authenticated user payload (login / GET /api/v1/auth/me) to decide
    /// which screen (legacy vs new) a given user is routed to.
    /// </summary>
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260708000000_AddUserFeatureFlags")]
    public partial class AddUserFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'users'
                          AND column_name = 'enable_new_morning_round'
                    ) THEN
                        ALTER TABLE users ADD COLUMN enable_new_morning_round boolean NOT NULL DEFAULT false;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'users'
                          AND column_name = 'enable_new_maintenance_log'
                    ) THEN
                        ALTER TABLE users ADD COLUMN enable_new_maintenance_log boolean NOT NULL DEFAULT false;
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
                          AND table_name = 'users'
                          AND column_name = 'enable_new_morning_round'
                    ) THEN
                        ALTER TABLE users DROP COLUMN enable_new_morning_round;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'users'
                          AND column_name = 'enable_new_maintenance_log'
                    ) THEN
                        ALTER TABLE users DROP COLUMN enable_new_maintenance_log;
                    END IF;
                END $$;
                """);
        }
    }
}

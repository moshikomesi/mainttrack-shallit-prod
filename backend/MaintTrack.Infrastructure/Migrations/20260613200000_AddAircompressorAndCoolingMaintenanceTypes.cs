using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260613200000_AddAircompressorAndCoolingMaintenanceTypes")]
    public partial class AddAircompressorAndCoolingMaintenanceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO maintenance_types (id, tenant_id, code, is_active, created_at)
                SELECT gen_random_uuid(), t.id, v.code, true, NOW()
                FROM tenants t
                CROSS JOIN (VALUES
                  ('aircompressor'),
                  ('cooling')
                ) AS v(code)
                WHERE EXISTS (
                    SELECT 1 FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
                )
                AND EXISTS (
                    SELECT 1 FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relname = 'tenants' AND c.relkind = 'r' AND n.nspname = 'public'
                )
                AND NOT EXISTS (
                  SELECT 1 FROM maintenance_types mt
                  WHERE mt.tenant_id = t.id AND mt.code = v.code
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM maintenance_types
                WHERE code IN ('aircompressor', 'cooling');
                """);
        }
    }
}

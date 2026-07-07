using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <summary>
    /// Deactivates machine.packing.stackers (מערמים) and
    /// machine.packing.externalBuckets (דליים חיצוניים) — not present in the
    /// factory's reference hierarchy map for בית אריזה (Packing House).
    /// </summary>
    [DbContext(typeof(MaintTrackDbContext))]
    [Migration("20260707234500_DeactivatePackingHouseExtras")]
    public partial class DeactivatePackingHouseExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE machines
                SET is_active = false, array_id = NULL
                WHERE name IN ('machine.packing.stackers', 'machine.packing.externalBuckets')
                  AND is_active = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE machines m
                SET is_active = true,
                    array_id = a.id
                FROM arrays a
                WHERE a.tenant_id = m.tenant_id
                  AND a.name_key = 'array.packing_house'
                  AND m.name IN ('machine.packing.stackers', 'machine.packing.externalBuckets');
                """);
        }
    }
}

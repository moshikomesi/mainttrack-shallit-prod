using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Domain.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MachineParameterPhotos;

/// <summary>
/// Stops catalog rollback while any photo still has a catalog association.
/// </summary>
public static class ParameterPhotoRollbackGuard
{
    public const string Message =
        "Cannot roll back the Parameter Photos catalog migration because machine_parameter_photos rows still reference parameter_id. Archive or explicitly relocate those photos before rolling back. Photo rows and image files were not deleted.";

    public const string Sql =
        """
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM machine_parameter_photos
                WHERE parameter_id IS NOT NULL
            ) THEN
                RAISE EXCEPTION 'Cannot roll back the Parameter Photos catalog migration because machine_parameter_photos rows still reference parameter_id. Archive or explicitly relocate those photos before rolling back. Photo rows and image files were not deleted.';
            END IF;
        END $$;
        """;

    public static async Task EnsureSafeAsync(DbContext dbContext, CancellationToken ct)
    {
        var blocked = await dbContext.Set<MachineParameterPhoto>()
            .IgnoreQueryFilters()
            .AnyAsync(photo => photo.ParameterId != null, ct);

        if (blocked)
        {
            throw new InvalidOperationException(Message);
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MachineParameterPhotos;

/// <summary>
/// Copies existing Parameter Photos visibility exclusions onto catalog parameters.
/// Shared machine and array rows are read only to preserve an exclusion that
/// already exists. They are not the catalog's runtime visibility source.
/// </summary>
public static class ParameterPhotoExclusionProjector
{
    public const string ProjectSql =
        """
        INSERT INTO parameter_photo_parameter_exclusions (id, tenant_id, parameter_id, created_at)
        SELECT gen_random_uuid(), parameter.tenant_id, parameter.id, NOW()
        FROM parameter_photo_parameters AS parameter
        WHERE (
                EXISTS (
                    SELECT 1
                    FROM machines AS machine
                    INNER JOIN machine_parameter_machine_visibility AS exclusion
                        ON exclusion.machine_id = machine.id
                       AND exclusion.tenant_id = machine.tenant_id
                    WHERE machine.tenant_id = parameter.tenant_id
                      AND machine.name = parameter.name_key
                )
                OR EXISTS (
                    SELECT 1
                    FROM machines AS machine
                    INNER JOIN array_feature_visibility AS visibility
                        ON visibility.array_id = machine.array_id
                       AND visibility.tenant_id = machine.tenant_id
                    WHERE machine.tenant_id = parameter.tenant_id
                      AND machine.name = parameter.name_key
                      AND visibility.feature_key = 'machine_parameter_photos'
                      AND visibility.is_visible = false
                )
            )
          AND NOT EXISTS (
                SELECT 1
                FROM parameter_photo_parameter_exclusions AS existing
                WHERE existing.tenant_id = parameter.tenant_id
                  AND existing.parameter_id = parameter.id
            );
        """;

    public const string UnlinkExcludedLegacyPhotosSql =
        """
        UPDATE machine_parameter_photos AS photo
        SET parameter_id = NULL
        WHERE photo.machine_id IS NOT NULL
          AND photo.parameter_id IS NOT NULL
          AND EXISTS (
                SELECT 1
                FROM parameter_photo_parameter_exclusions AS exclusion
                WHERE exclusion.parameter_id = photo.parameter_id
                  AND exclusion.tenant_id = photo.tenant_id
            );
        """;

    public const string TenantIntegrityGuardSql =
        """
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM parameter_photo_parameters AS parameter
                INNER JOIN parameter_photo_groups AS catalog_group
                    ON catalog_group.id = parameter.group_id
                WHERE parameter.tenant_id <> catalog_group.tenant_id
            ) THEN
                RAISE EXCEPTION 'Cannot enforce tenant-bound Parameter Photos relationships: a parameter references a group from another tenant. No rows were changed.';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM machine_parameter_photos AS photo
                INNER JOIN parameter_photo_parameters AS parameter
                    ON parameter.id = photo.parameter_id
                WHERE photo.parameter_id IS NOT NULL
                  AND photo.tenant_id <> parameter.tenant_id
            ) THEN
                RAISE EXCEPTION 'Cannot enforce tenant-bound Parameter Photos relationships: a photo references a parameter from another tenant. No rows were changed.';
            END IF;
        END $$;
        """;

    public static async Task ProjectAsync(MaintTrackDbContext dbContext, CancellationToken ct)
    {
        var parameters = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(parameter => new { parameter.Id, parameter.TenantId, parameter.NameKey })
            .ToListAsync(ct);

        var existing = await dbContext.ParameterPhotoParameterExclusions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(exclusion => new { exclusion.TenantId, exclusion.ParameterId })
            .ToListAsync(ct);
        var existingKeys = existing
            .Select(exclusion => (exclusion.TenantId, exclusion.ParameterId))
            .ToHashSet();

        var excludedMachineNames = await dbContext.MachineParameterMachineExclusions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Join(
                dbContext.Machines.IgnoreQueryFilters().AsNoTracking(),
                exclusion => exclusion.MachineId,
                machine => machine.Id,
                (exclusion, machine) => new { exclusion.TenantId, MachineTenantId = machine.TenantId, machine.Name })
            .Where(row => row.TenantId == row.MachineTenantId)
            .Select(row => new { row.TenantId, row.Name })
            .ToListAsync(ct);

        var hiddenArrayNames = await (
                from visibility in dbContext.ArrayFeatureVisibilities.IgnoreQueryFilters().AsNoTracking()
                join machine in dbContext.Machines.IgnoreQueryFilters().AsNoTracking()
                    on visibility.ArrayId equals machine.ArrayId
                where visibility.FeatureKey == ArrayFeatureVisibility.MachineParameterPhotosFeatureKey
                    && !visibility.IsVisible
                    && visibility.TenantId == machine.TenantId
                select new { visibility.TenantId, machine.Name })
            .ToListAsync(ct);

        var hiddenNames = excludedMachineNames
            .Select(row => (row.TenantId, row.Name))
            .Concat(hiddenArrayNames.Select(row => (row.TenantId, row.Name)))
            .ToHashSet();

        var added = false;
        foreach (var parameter in parameters)
        {
            if (!hiddenNames.Contains((parameter.TenantId, parameter.NameKey)) ||
                !existingKeys.Add((parameter.TenantId, parameter.Id)))
            {
                continue;
            }

            dbContext.ParameterPhotoParameterExclusions.Add(new ParameterPhotoParameterExclusion
            {
                Id = Guid.NewGuid(),
                TenantId = parameter.TenantId,
                ParameterId = parameter.Id,
                CreatedAt = DateTime.UtcNow
            });
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(ct);
        }

        var excludedParameterIds = await dbContext.ParameterPhotoParameterExclusions
            .IgnoreQueryFilters()
            .Select(exclusion => new { exclusion.TenantId, exclusion.ParameterId })
            .ToListAsync(ct);
        var excludedKeys = excludedParameterIds
            .Select(exclusion => (exclusion.TenantId, exclusion.ParameterId))
            .ToHashSet();

        var linkedLegacyPhotos = await dbContext.MachineParameterPhotos
            .IgnoreQueryFilters()
            .Where(photo => photo.MachineId != null && photo.ParameterId != null)
            .ToListAsync(ct);

        var unlinked = false;
        foreach (var photo in linkedLegacyPhotos)
        {
            if (photo.ParameterId is Guid parameterId &&
                excludedKeys.Contains((photo.TenantId, parameterId)))
            {
                photo.ParameterId = null;
                unlinked = true;
            }
        }

        if (unlinked)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }
}

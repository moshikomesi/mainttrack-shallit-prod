using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MachineParameterPhotos;

public sealed record ParameterPhotoLinkResult(
    int LinkedCount,
    IReadOnlyList<string> AmbiguousMachineNames);

/// <summary>
/// Links a legacy photo to a catalog parameter only when one machine in the
/// same tenant has a name equal to the parameter name key. Image URLs,
/// machine ids, and existing parameter ids are left unchanged. First Hopper,
/// Second Hopper, and the Onion System Hopper are never linked, and nothing
/// is linked automatically to the standalone Hopper parameter.
/// </summary>
public static class ParameterPhotoLegacyLinker
{
    private const string AssignmentAndGuards =
        """
        SET parameter_id = (
            SELECT parameter.id
            FROM parameter_photo_parameters AS parameter
            INNER JOIN machines AS machine
                ON machine.id = machine_parameter_photos.machine_id
               AND machine.tenant_id = parameter.tenant_id
               AND machine.name = parameter.name_key
            WHERE parameter.tenant_id = machine_parameter_photos.tenant_id
              AND parameter.name_key <> 'parameterPhoto.parameter.hopper'
              AND machine.name NOT IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
              AND (
                  SELECT COUNT(*)
                  FROM machines AS named
                  WHERE named.tenant_id = machine.tenant_id
                    AND named.name = machine.name
              ) = 1
            LIMIT 1
        )
        WHERE parameter_id IS NULL
          AND machine_id IS NOT NULL
          AND EXISTS (
              SELECT 1
              FROM parameter_photo_parameters AS parameter
              INNER JOIN machines AS machine
                  ON machine.id = machine_parameter_photos.machine_id
                 AND machine.tenant_id = parameter.tenant_id
                 AND machine.name = parameter.name_key
              WHERE parameter.tenant_id = machine_parameter_photos.tenant_id
                AND parameter.name_key <> 'parameterPhoto.parameter.hopper'
                AND machine.name NOT IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
                AND (
                    SELECT COUNT(*)
                    FROM machines AS named
                    WHERE named.tenant_id = machine.tenant_id
                      AND named.name = machine.name
                ) = 1
          )
          AND NOT EXISTS (
              SELECT 1
              FROM machine_parameter_photos AS conflict
              WHERE conflict.id <> machine_parameter_photos.id
                AND conflict.sort_order = machine_parameter_photos.sort_order
                AND conflict.parameter_id = (
                    SELECT parameter.id
                    FROM parameter_photo_parameters AS parameter
                    INNER JOIN machines AS machine
                        ON machine.id = machine_parameter_photos.machine_id
                       AND machine.tenant_id = parameter.tenant_id
                       AND machine.name = parameter.name_key
                    WHERE parameter.tenant_id = machine_parameter_photos.tenant_id
                      AND parameter.name_key <> 'parameterPhoto.parameter.hopper'
                      AND machine.name NOT IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
                    LIMIT 1
                )
          )
        """;

    /// <summary>
    /// Used by the catalog migration, which runs before the exclusion table exists.
    /// </summary>
    public const string MigrationLinkSql =
        "UPDATE machine_parameter_photos\n" + AssignmentAndGuards + ";";

    /// <summary>
    /// Runtime link. Skips parameters hidden by a Parameter Photos exclusion.
    /// </summary>
    public const string RuntimeLinkSql =
        "UPDATE machine_parameter_photos\n" + AssignmentAndGuards + """

          AND NOT EXISTS (
              SELECT 1
              FROM parameter_photo_parameter_exclusions AS exclusion
              INNER JOIN parameter_photo_parameters AS excluded_parameter
                  ON excluded_parameter.id = exclusion.parameter_id
                 AND excluded_parameter.tenant_id = exclusion.tenant_id
              INNER JOIN machines AS excluded_machine
                  ON excluded_machine.id = machine_parameter_photos.machine_id
                 AND excluded_machine.tenant_id = excluded_parameter.tenant_id
                 AND excluded_machine.name = excluded_parameter.name_key
              WHERE exclusion.tenant_id = machine_parameter_photos.tenant_id
          )
        """ + ";";

    public static async Task<ParameterPhotoLinkResult> LinkAsync(
        MaintTrackDbContext dbContext,
        Guid? tenantId,
        CancellationToken ct)
    {
        var machines = dbContext.Machines.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is Guid scopedTenant)
        {
            machines = machines.Where(machine => machine.TenantId == scopedTenant);
        }

        var ambiguousMachineNames = await machines
            .GroupBy(machine => new { machine.TenantId, machine.Name })
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);

        var sql = tenantId is null
            ? RuntimeLinkSql
            : RuntimeLinkSql.TrimEnd().TrimEnd(';') + "\n  AND machine_parameter_photos.tenant_id = {0};";

        var linkedCount = tenantId is Guid tenant
            ? await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { tenant }, ct)
            : await dbContext.Database.ExecuteSqlRawAsync(sql, ct);

        return new ParameterPhotoLinkResult(linkedCount, ambiguousMachineNames);
    }
}

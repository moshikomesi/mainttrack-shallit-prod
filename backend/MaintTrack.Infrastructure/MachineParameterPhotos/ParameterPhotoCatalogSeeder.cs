using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MachineParameterPhotos;

public static class ParameterPhotoCatalogSeeder
{
    public static async Task<ParameterPhotoLinkResult?> EnsureAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var inserted = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!await AddMissingAsync(dbContext, tenantId, ct))
                {
                    await RetireSupersededParametersAsync(dbContext, tenantId, ct);
                    await SyncCanonicalSortOrdersAsync(dbContext, tenantId, ct);
                    return await LinkMatchableUnlinkedPhotosAsync(dbContext, tenantId, ct);
                }

                await dbContext.SaveChangesAsync(ct);
                inserted = true;
                break;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                DetachCatalogEntries(dbContext);
                if (attempt == 2 && !await CanonicalCatalogExistsAsync(dbContext, tenantId, ct))
                {
                    throw;
                }
            }
        }

        if (!inserted)
        {
            return null;
        }

        // Link photos whose machine name uniquely matches a catalog parameter.
        // Photos with no such match are left unchanged for an explicit resolution.
        await RetireSupersededParametersAsync(dbContext, tenantId, ct);
        var linked = await LinkMatchableUnlinkedPhotosAsync(dbContext, tenantId, ct);
        await SyncCanonicalSortOrdersAsync(dbContext, tenantId, ct);
        return linked;
    }

    private static async Task<ParameterPhotoLinkResult?> LinkMatchableUnlinkedPhotosAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!await HasMatchableUnlinkedPhotoAsync(dbContext, tenantId, ct))
        {
            return null;
        }

        return await ParameterPhotoLegacyLinker.LinkAsync(dbContext, tenantId, ct);
    }

    /// <summary>
    /// True when an unlinked photo can be attached by the existing name rule:
    /// one machine in the tenant has <c>machines.name</c> equal to an active,
    /// non-excluded parameter, the parameter is not the standalone Hopper, and
    /// the photo's sort order is free on that parameter. Hopper machine names
    /// are never matchable.
    /// </summary>
    private static async Task<bool> HasMatchableUnlinkedPhotoAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var unlinked = await dbContext.MachineParameterPhotos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(photo => photo.TenantId == tenantId && photo.ParameterId == null && photo.MachineId != null)
            .Select(photo => new { MachineId = photo.MachineId!.Value, photo.SortOrder })
            .ToListAsync(ct);
        if (unlinked.Count == 0)
        {
            return false;
        }

        var machines = await dbContext.Machines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(machine => machine.TenantId == tenantId)
            .Select(machine => new { machine.Id, machine.Name })
            .ToListAsync(ct);
        var machinesById = machines.ToDictionary(machine => machine.Id);
        var nameCounts = machines
            .GroupBy(machine => machine.Name)
            .ToDictionary(group => group.Key, group => group.Count());

        var excludedParameterIds = await dbContext.ParameterPhotoParameterExclusions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(exclusion => exclusion.TenantId == tenantId)
            .Select(exclusion => exclusion.ParameterId)
            .ToListAsync(ct);
        var excluded = excludedParameterIds.ToHashSet();

        var parameters = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(parameter =>
                parameter.TenantId == tenantId &&
                parameter.IsActive &&
                parameter.NameKey != ParameterPhotoCatalog.HopperKey)
            .Select(parameter => new { parameter.Id, parameter.NameKey })
            .ToListAsync(ct);
        var parametersByName = parameters
            .Where(parameter => !excluded.Contains(parameter.Id))
            .ToDictionary(parameter => parameter.NameKey);

        var occupiedSorts = await dbContext.MachineParameterPhotos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(photo => photo.TenantId == tenantId && photo.ParameterId != null)
            .Select(photo => new { ParameterId = photo.ParameterId!.Value, photo.SortOrder })
            .ToListAsync(ct);
        var occupied = occupiedSorts
            .Select(photo => (photo.ParameterId, photo.SortOrder))
            .ToHashSet();

        foreach (var photo in unlinked)
        {
            if (!machinesById.TryGetValue(photo.MachineId, out var machine) ||
                ParameterPhotoLegacyLinker.DeniedMachineNames.Contains(machine.Name) ||
                !nameCounts.TryGetValue(machine.Name, out var nameCount) ||
                nameCount != 1 ||
                !parametersByName.TryGetValue(machine.Name, out var parameter) ||
                occupied.Contains((parameter.Id, photo.SortOrder)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static async Task<bool> AddMissingAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var existingGroupKeys = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(group => group.TenantId == tenantId)
            .Select(group => group.NameKey)
            .ToListAsync(ct);

        var existingParameterKeys = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(parameter => parameter.TenantId == tenantId)
            .Select(parameter => parameter.NameKey)
            .ToListAsync(ct);

        var groupIds = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(group => group.TenantId == tenantId)
            .ToDictionaryAsync(group => group.NameKey, group => group.Id, ct);

        var parameterKeySet = existingParameterKeys.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = false;

        foreach (var groupDefinition in ParameterPhotoCatalog.Groups)
        {
            if (!groupIds.TryGetValue(groupDefinition.NameKey, out var groupId))
            {
                groupId = Guid.NewGuid();
                groupIds[groupDefinition.NameKey] = groupId;
                dbContext.ParameterPhotoGroups.Add(new ParameterPhotoGroup
                {
                    Id = groupId,
                    TenantId = tenantId,
                    NameKey = groupDefinition.NameKey,
                    SortOrder = groupDefinition.SortOrder,
                    IsActive = true,
                    CreatedAt = now
                });
                added = true;
            }

            foreach (var parameterDefinition in groupDefinition.Parameters)
            {
                if (!parameterKeySet.Add(parameterDefinition.NameKey))
                {
                    continue;
                }

                dbContext.ParameterPhotoParameters.Add(new ParameterPhotoParameter
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    GroupId = groupId,
                    NameKey = parameterDefinition.NameKey,
                    SortOrder = parameterDefinition.SortOrder,
                    IsActive = true,
                    CreatedAt = now
                });
                added = true;
            }
        }

        return added;
    }

    /// <summary>
    /// Hides the retired Gan Shomron cooling parameter on catalogs created before
    /// Gan Shomron was its own group. The row and any existing photo association
    /// are left in place; the parameter is no longer offered for assignment.
    /// </summary>
    private static async Task RetireSupersededParametersAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var retiredKeys = new[]
        {
            ParameterPhotoCatalog.RetiredGanShomronCoolingKey,
            ParameterPhotoCatalog.RetiredDuplicateBagPullersKey
        };
        var retired = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .Where(parameter =>
                parameter.TenantId == tenantId &&
                retiredKeys.Contains(parameter.NameKey) &&
                parameter.IsActive)
            .ToListAsync(ct);
        if (retired.Count == 0)
        {
            return;
        }

        foreach (var parameter in retired)
        {
            parameter.IsActive = false;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static async Task SyncCanonicalSortOrdersAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var groups = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .Where(group => group.TenantId == tenantId)
            .ToListAsync(ct);
        var parameters = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .Where(parameter => parameter.TenantId == tenantId)
            .ToListAsync(ct);
        var changed = false;

        foreach (var groupDefinition in ParameterPhotoCatalog.Groups)
        {
            var group = groups.FirstOrDefault(item => item.NameKey == groupDefinition.NameKey);
            if (group is not null && group.SortOrder != groupDefinition.SortOrder)
            {
                group.SortOrder = groupDefinition.SortOrder;
                changed = true;
            }

            foreach (var parameterDefinition in groupDefinition.Parameters)
            {
                var parameter = parameters.FirstOrDefault(item => item.NameKey == parameterDefinition.NameKey);
                if (parameter is null || group is null)
                {
                    continue;
                }

                if (parameter.GroupId != group.Id)
                {
                    parameter.GroupId = group.Id;
                    changed = true;
                }

                if (parameter.SortOrder != parameterDefinition.SortOrder)
                {
                    parameter.SortOrder = parameterDefinition.SortOrder;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static async Task<bool> CanonicalCatalogExistsAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        var groupKeys = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(group => group.TenantId == tenantId)
            .Select(group => group.NameKey)
            .ToListAsync(ct);
        var parameterKeys = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(parameter => parameter.TenantId == tenantId)
            .Select(parameter => parameter.NameKey)
            .ToListAsync(ct);
        var groups = groupKeys.ToHashSet(StringComparer.Ordinal);
        var parameters = parameterKeys.ToHashSet(StringComparer.Ordinal);

        return ParameterPhotoCatalog.Groups.All(group =>
            groups.Contains(group.NameKey) &&
            group.Parameters.All(parameter => parameters.Contains(parameter.NameKey)));
    }

    private static void DetachCatalogEntries(MaintTrackDbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<ParameterPhotoParameter>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<ParameterPhotoGroup>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("23505", StringComparison.Ordinal);
    }
}

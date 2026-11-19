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
    public static async Task EnsureAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!await AddMissingAsync(dbContext, tenantId, ct))
                {
                    await SyncCanonicalSortOrdersAsync(dbContext, tenantId, ct);
                    return;
                }

                await dbContext.SaveChangesAsync(ct);
                await SyncCanonicalSortOrdersAsync(dbContext, tenantId, ct);
                return;
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
                if (parameter is not null && parameter.SortOrder != parameterDefinition.SortOrder)
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

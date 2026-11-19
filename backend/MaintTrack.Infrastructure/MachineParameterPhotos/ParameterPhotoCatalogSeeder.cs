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
        var existingGroupKeys = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .Where(group => group.TenantId == tenantId)
            .Select(group => group.NameKey)
            .ToListAsync(ct);

        var existingParameterKeys = await dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .Where(parameter => parameter.TenantId == tenantId)
            .Select(parameter => parameter.NameKey)
            .ToListAsync(ct);

        var groupIds = await dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .Where(group => group.TenantId == tenantId)
            .ToDictionaryAsync(group => group.NameKey, group => group.Id, ct);

        var groupKeySet = existingGroupKeys.ToHashSet(StringComparer.Ordinal);
        var parameterKeySet = existingParameterKeys.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = false;

        foreach (var groupDefinition in ParameterPhotoCatalog.Groups)
        {
            if (!groupIds.TryGetValue(groupDefinition.NameKey, out var groupId))
            {
                groupId = Guid.NewGuid();
                groupIds[groupDefinition.NameKey] = groupId;
                groupKeySet.Add(groupDefinition.NameKey);
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

        if (added)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MorningRound;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MorningRound;

/// <summary>
/// Provides the tenant-specific Morning Round checklist template.
/// </summary>
public sealed class MorningRoundTemplateService : IMorningRoundTemplateService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public MorningRoundTemplateService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IReadOnlyList<MorningRoundTemplateItemDto>> GetTemplateAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
        {
            return Array.Empty<MorningRoundTemplateItemDto>();
        }

        var items = await _dbContext.MorningRoundTemplateItems
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new MorningRoundTemplateItemDto(
                x.Id,
                x.TranslationKey,
                x.SortOrder))
            .ToListAsync(ct);

        return items;
    }
}


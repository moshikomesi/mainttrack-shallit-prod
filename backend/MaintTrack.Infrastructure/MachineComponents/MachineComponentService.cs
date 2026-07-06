using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MachineComponents;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MachineComponents;

public sealed class MachineComponentService : IMachineComponentService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public MachineComponentService(MaintTrackDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IReadOnlyList<MachineComponentDto>> GetByMachineIdAsync(
        Guid machineId,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        var machineExists = await _dbContext.Machines
            .AsNoTracking()
            .AnyAsync(m => m.Id == machineId && m.TenantId == tenantId && m.IsActive, ct);

        if (!machineExists)
        {
            throw new InvalidOperationException("Machine not found.");
        }

        return await (
            from mapping in _dbContext.MachineComponentMappings.AsNoTracking()
            join component in _dbContext.MachineComponents.AsNoTracking()
                on mapping.ComponentId equals component.Id
            where mapping.TenantId == tenantId
                  && mapping.MachineId == machineId
                  && mapping.IsActive
                  && component.IsActive
            orderby mapping.SortOrder, component.SortOrder, component.Code
            select new MachineComponentDto(
                component.Id,
                component.Code,
                component.NameKey,
                mapping.SortOrder))
            .ToListAsync(ct);
    }
}

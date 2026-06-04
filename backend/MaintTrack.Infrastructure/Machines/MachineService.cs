using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Machines;
using MaintTrack.Domain.Machines;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Machines;

/// <summary>
/// Infrastructure implementation of <see cref="IMachineService" /> backed by EF Core.
/// </summary>
public sealed class MachineService : IMachineService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public MachineService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IReadOnlyList<MachineDto>> GetAllAsync(CancellationToken ct)
    {
        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new MachineDto(
                m.Id,
                m.Name,
                m.Code,
                m.Description))
            .ToListAsync(ct);

        return machines;
    }

    public async Task<Guid> CreateAsync(CreateMachineRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        var machine = new Machine
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId.Value,
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.Machines.AddAsync(machine, ct);
        await _dbContext.SaveChangesAsync(ct);

        return machine.Id;
    }
}


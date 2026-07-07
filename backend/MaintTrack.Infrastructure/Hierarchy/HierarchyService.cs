using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Arrays;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Hierarchy;

public sealed class HierarchyService : IHierarchyService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public HierarchyService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IReadOnlyList<HierarchyArrayDto>> GetHierarchyAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var arrays = await _dbContext.Arrays
            .AsNoTracking()
            .Where(a => a.IsActive && a.TenantId == tenantId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.NameKey)
            .ToListAsync(ct);

        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(m => m.IsActive && m.TenantId == tenantId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .Select(m => new MachineHierarchyRow(m.Id, m.Name, m.ArrayId))
            .ToListAsync(ct);

        await transaction.CommitAsync(ct);

        var hierarchy = BuildHierarchy(arrays, machines);
        VerifyHierarchyInvariant(machines, hierarchy);

        return hierarchy;
    }

    internal static IReadOnlyList<HierarchyArrayDto> BuildHierarchy(
        IReadOnlyList<WorkGroup> arrays,
        IReadOnlyList<MachineHierarchyRow> machines)
    {
        var validArrayIds = arrays.Select(a => a.Id).ToHashSet();

        var machinesByArrayId = arrays.ToDictionary(
            a => a.Id,
            _ => new List<MachineHierarchyRow>());

        var unassigned = new List<MachineHierarchyRow>();

        foreach (var machine in machines)
        {
            if (machine.ArrayId is { } arrayId && validArrayIds.Contains(arrayId))
            {
                machinesByArrayId[arrayId].Add(machine);
            }
            else
            {
                unassigned.Add(machine);
            }
        }

        var hierarchy = new List<HierarchyArrayDto>(arrays.Count + (unassigned.Count > 0 ? 1 : 0));

        foreach (var array in arrays)
        {
            hierarchy.Add(new HierarchyArrayDto(
                array.Id,
                array.NameKey,
                MapMachines(machinesByArrayId[array.Id])));
        }

        if (unassigned.Count > 0)
        {
            hierarchy.Add(new HierarchyArrayDto(
                null,
                HierarchyDefaults.UnassignedNameKey,
                MapMachines(unassigned)));
        }

        return hierarchy;
    }

    private static void VerifyHierarchyInvariant(
        IReadOnlyList<MachineHierarchyRow> sourceMachines,
        IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        var outputCount = hierarchy.Sum(group => group.Machines.Count);
        if (outputCount != sourceMachines.Count)
        {
            throw new InvalidOperationException(
                "Hierarchy invariant violated: output machine count does not match source.");
        }

        var seenMachineIds = new HashSet<Guid>();
        foreach (var group in hierarchy)
        {
            foreach (var machine in group.Machines)
            {
                if (!seenMachineIds.Add(machine.Id))
                {
                    throw new InvalidOperationException(
                        "Hierarchy invariant violated: duplicate machine in output.");
                }
            }
        }
    }

    private static IReadOnlyList<HierarchyMachineDto> MapMachines(
        IReadOnlyList<MachineHierarchyRow> machines)
    {
        if (machines.Count == 0)
        {
            return Array.Empty<HierarchyMachineDto>();
        }

        return machines
            .Select(m => new HierarchyMachineDto(m.Id, m.Name))
            .ToList();
    }

    public sealed record MachineHierarchyRow(Guid Id, string Name, Guid? ArrayId);
}

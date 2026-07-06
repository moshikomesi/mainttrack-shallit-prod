using MaintTrack.Domain.Machines;
using MaintTrack.Tests.Hierarchy.Support;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyTenantIsolationTests : IAsyncLifetime
{
    private HierarchyTestContext _ctx = null!;
    private readonly Guid _otherTenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _ctx = new HierarchyTestContext();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task GetHierarchyAsync_ExcludesOtherTenantArraysAndMachines()
    {
        var tenantArray = HierarchyTestData.CreateArray(_ctx.TenantId, "array.mine");
        var otherArray = HierarchyTestData.CreateArray(_otherTenantId, "array.other");
        var tenantMachine = HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.mine", "M", tenantArray.Id);
        var otherMachine = HierarchyTestData.CreateMachine(_otherTenantId, "machine.other", "O", otherArray.Id);

        await _ctx.SaveAsync(tenantArray, otherArray, tenantMachine, otherMachine);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        var machineIds = HierarchyTestData.FlattenMachines(hierarchy).Select(m => m.Id).ToHashSet();
        Assert.Contains(tenantMachine.Id, machineIds);
        Assert.DoesNotContain(otherMachine.Id, machineIds);
        Assert.DoesNotContain(hierarchy, g => g.NameKey == "array.other");
    }

    [Fact]
    public async Task GetHierarchyAsync_WithManualTenantBypassFilter_StillScopesByExplicitTenantPredicate()
    {
        var tenantArray = HierarchyTestData.CreateArray(_ctx.TenantId, "array.mine");
        var otherMachine = HierarchyTestData.CreateMachine(_otherTenantId, "machine.other", "O", tenantArray.Id);

        await _ctx.SaveAsync(tenantArray, otherMachine);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Empty(HierarchyTestData.FlattenMachines(hierarchy));
        Assert.Single(hierarchy);
        Assert.Empty(hierarchy[0].Machines);
    }

    [Fact]
    public async Task QueryFilter_BlocksCrossTenantMachineReads()
    {
        var otherMachine = HierarchyTestData.CreateMachine(_otherTenantId, "machine.other", "O");
        await _ctx.SaveAsync(otherMachine);

        var visible = await _ctx.DbContext.Machines.AsNoTracking().ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task QueryFilter_BlocksCrossTenantArrayReads()
    {
        var otherArray = HierarchyTestData.CreateArray(_otherTenantId, "array.other");
        await _ctx.SaveAsync(otherArray);

        var visible = await _ctx.DbContext.Arrays.AsNoTracking().ToListAsync();

        Assert.Empty(visible);
    }
}

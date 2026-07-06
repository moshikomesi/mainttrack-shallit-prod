using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Machines;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyServiceIntegrationTests : IAsyncLifetime
{
    private HierarchyTestContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new HierarchyTestContext();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task GetHierarchyAsync_PersistsSnapshotAcrossArraysAndMachines()
    {
        var arrayA = HierarchyTestData.CreateArray(_ctx.TenantId, "array.a", sortOrder: 0);
        var arrayB = HierarchyTestData.CreateArray(_ctx.TenantId, "array.b", sortOrder: 1);
        var machineA = HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.a", "A", arrayA.Id);
        var machineB = HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.b", "B", arrayB.Id);
        var machineUnassigned = HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.u", "U", null);
        var machineOrphan = HierarchyTestData.CreateMachine(
            _ctx.TenantId,
            "machine.o",
            "O",
            Guid.NewGuid());

        await _ctx.SaveAsync(arrayA, arrayB, machineA, machineB, machineUnassigned, machineOrphan);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Equal(3, hierarchy.Count);
        Assert.Equal(2, hierarchy.Count(g => g.ArrayId is not null));
        Assert.Single(hierarchy, g => g.ArrayId is null);

        var sourceRows = new[]
        {
            new HierarchyService.MachineHierarchyRow(machineA.Id, machineA.Name, machineA.ArrayId),
            new HierarchyService.MachineHierarchyRow(machineB.Id, machineB.Name, machineB.ArrayId),
            new HierarchyService.MachineHierarchyRow(machineUnassigned.Id, machineUnassigned.Name, machineUnassigned.ArrayId),
            new HierarchyService.MachineHierarchyRow(machineOrphan.Id, machineOrphan.Name, machineOrphan.ArrayId)
        };

        HierarchyInvariantAssertions.AssertCompleteHierarchy(sourceRows, hierarchy);
    }

    [Fact]
    public async Task GetHierarchyAsync_WithoutTenant_Throws()
    {
        _ctx.TenantContext.TenantId = null;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ctx.Service.GetHierarchyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetHierarchyAsync_IsDeterministicAcrossCalls()
    {
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.a");
        var machines = Enumerable.Range(0, 20)
            .Select(i => HierarchyTestData.CreateMachine(_ctx.TenantId, $"machine.{i:D2}", $"C{i}", i % 2 == 0 ? array.Id : null))
            .ToArray();

        await _ctx.SaveAsync(array);
        await _ctx.SaveAsync(machines);

        var first = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);
        var second = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Equal(
            HierarchyTestData.FlattenMachines(first).Select(m => m.Id),
            HierarchyTestData.FlattenMachines(second).Select(m => m.Id));
    }

    [Fact]
    public async Task GetHierarchyAsync_ConcurrentRequests_ReturnSameGrouping()
    {
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.a");
        var machines = Enumerable.Range(0, 50)
            .Select(i => HierarchyTestData.CreateMachine(_ctx.TenantId, $"machine.{i}", $"C{i}", array.Id))
            .ToArray();

        await _ctx.SaveAsync(array);
        await _ctx.SaveAsync(machines);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => _ctx.Service.GetHierarchyAsync(CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var baseline = HierarchyTestData.FlattenMachines(results[0]).Select(m => m.Id).ToList();

        foreach (var result in results.Skip(1))
        {
            Assert.Equal(baseline, HierarchyTestData.FlattenMachines(result).Select(m => m.Id));
        }
    }
}

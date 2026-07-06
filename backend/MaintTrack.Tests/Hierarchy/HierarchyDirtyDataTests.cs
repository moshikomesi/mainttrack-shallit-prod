using MaintTrack.Application.Hierarchy;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyDirtyDataTests : IAsyncLifetime
{
    private HierarchyTestContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new HierarchyTestContext();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task PartialMigrationState_AllMachinesUnassigned_NoCrash()
    {
        var machines = new[]
        {
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.one", "1", null),
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.two", "2", null)
        };

        await _ctx.SaveAsync(machines);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Single(hierarchy);
        Assert.Equal(2, hierarchy[0].Machines.Count);
    }

    [Fact]
    public async Task RandomInvalidArrayIds_AllRoutedToUnassigned()
    {
        var machines = Enumerable.Range(0, 10)
            .Select(i => HierarchyTestData.CreateMachine(
                _ctx.TenantId,
                $"machine.{i}",
                $"C{i}",
                Guid.NewGuid()))
            .ToArray();

        await _ctx.SaveAsync(machines);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);
        var unassigned = Assert.Single(hierarchy);

        Assert.Equal(HierarchyDefaults.UnassignedNameKey, unassigned.NameKey);
        Assert.Equal(10, unassigned.Machines.Count);
    }

    [Fact]
    public async Task MachinesExistWithoutArrayRows_ReturnsUnassignedOnly()
    {
        var machines = new[]
        {
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.a", "A", Guid.NewGuid()),
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.b", "B", null)
        };

        await _ctx.SaveAsync(machines);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(2, unassigned.Machines.Count);
    }

    [Fact]
    public async Task MixedValidInvalidAndNullArrayIds_NeverDropsMachines()
    {
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.valid");
        var machines = new[]
        {
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.valid", "V", array.Id),
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.null", "N", null),
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.invalid", "I", Guid.NewGuid())
        };

        await _ctx.SaveAsync(array);
        await _ctx.SaveAsync(machines);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Equal(2, hierarchy.Count);
        Assert.Equal(3, HierarchyTestData.FlattenMachines(hierarchy).Count);
    }

    [Fact]
    public async Task InactiveArrayInDatabase_MachineWithThatIdGoesToUnassigned()
    {
        var inactiveArray = HierarchyTestData.CreateArray(
            _ctx.TenantId,
            "array.inactive",
            isActive: false);
        var machine = HierarchyTestData.CreateMachine(
            _ctx.TenantId,
            "machine.refInactive",
            "RI",
            inactiveArray.Id);

        await _ctx.SaveAsync(inactiveArray, machine);

        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(machine.Id, Assert.Single(unassigned.Machines).Id);
    }
}

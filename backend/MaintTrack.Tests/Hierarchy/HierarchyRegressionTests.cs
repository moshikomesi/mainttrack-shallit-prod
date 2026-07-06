using MaintTrack.Application.Machines;
using MaintTrack.Tests.Hierarchy.Support;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyRegressionTests : IAsyncLifetime
{
    private HierarchyTestContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new HierarchyTestContext();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task MachineService_GetAllAsync_UnchangedByHierarchyData()
    {
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.regression");
        var machines = new[]
        {
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.one", "1", array.Id),
            HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.two", "2", null)
        };

        await _ctx.SaveAsync(array);
        await _ctx.SaveAsync(machines);

        var machineService = new MaintTrack.Infrastructure.Machines.MachineService(
            _ctx.DbContext,
            _ctx.TenantContext);

        IReadOnlyList<MachineDto> flatMachines = await machineService.GetAllAsync(CancellationToken.None);
        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Equal(2, flatMachines.Count);
        Assert.Equal(flatMachines.Count, HierarchyTestData.FlattenMachines(hierarchy).Count);
        Assert.Equal(
            flatMachines.Select(m => m.Id).OrderBy(id => id),
            HierarchyTestData.FlattenMachines(hierarchy).Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Hierarchy_DoesNotMutateMachineRecords()
    {
        var machine = HierarchyTestData.CreateMachine(_ctx.TenantId, "machine.stable", "S", Guid.NewGuid());
        await _ctx.SaveAsync(machine);

        await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        var reloaded = await _ctx.DbContext.Machines.AsNoTracking().SingleAsync(m => m.Id == machine.Id);
        Assert.Equal(machine.ArrayId, reloaded.ArrayId);
        Assert.Equal(machine.Name, reloaded.Name);
        Assert.Equal(machine.Code, reloaded.Code);
    }
}

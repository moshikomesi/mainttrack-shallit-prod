using MaintTrack.Domain.Machines;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyPerformanceTests : IAsyncLifetime
{
    private HierarchyTestContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new HierarchyTestContext();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task GetHierarchyAsync_UsesAtMostTwoSelectQueries()
    {
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.perf");
        var machines = Enumerable.Range(0, 100)
            .Select(i => HierarchyTestData.CreateMachine(_ctx.TenantId, $"machine.{i}", $"C{i}", i % 3 == 0 ? array.Id : null))
            .ToArray();

        await _ctx.SaveAsync(array);
        await _ctx.SaveAsync(machines);

        _ctx.QueryCounter.Reset();
        await _ctx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.InRange(_ctx.QueryCounter.SelectCount, 1, 2);
    }

    [Fact]
    public async Task GetHierarchyAsync_ScalesToTenThousandMachines()
    {
        const int machineCount = 10_000;
        var array = HierarchyTestData.CreateArray(_ctx.TenantId, "array.large");

        await _ctx.SaveAsync(array);

        var machines = new List<Machine>(machineCount);
        for (var i = 0; i < machineCount; i++)
        {
            machines.Add(HierarchyTestData.CreateMachine(
                _ctx.TenantId,
                $"machine.{i:D5}",
                $"C{i:D5}",
                i % 2 == 0 ? array.Id : null));
        }

        _ctx.DbContext.Machines.AddRange(machines);
        await _ctx.DbContext.SaveChangesAsync();

        _ctx.QueryCounter.Reset();
        var started = DateTime.UtcNow;
        var hierarchy = await _ctx.Service.GetHierarchyAsync(CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(machineCount, HierarchyTestData.FlattenMachines(hierarchy).Count);
        Assert.InRange(_ctx.QueryCounter.SelectCount, 1, 2);
        Assert.True(elapsed < TimeSpan.FromSeconds(30), $"Hierarchy build took {elapsed.TotalSeconds:F2}s");
    }
}

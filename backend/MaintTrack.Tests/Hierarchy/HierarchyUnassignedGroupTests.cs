using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Arrays;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyUnassignedGroupTests
{
    [Fact]
    public void UnassignedGroup_UsesStableNameKey()
    {
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            new[] { HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", null) });

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(HierarchyDefaults.UnassignedNameKey, unassigned.NameKey);
        Assert.Null(unassigned.ArrayId);
    }

    [Fact]
    public void UnassignedGroup_AppearsOnlyOnce()
    {
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            new[]
            {
                HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", null),
                HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.two", Guid.NewGuid())
            });

        Assert.Single(hierarchy, g => g.ArrayId is null);
    }

    [Fact]
    public void UnassignedGroup_PreservesDeterministicMachineOrder()
    {
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var machines = new[]
        {
            HierarchyTestData.MachineRow(id1, "machine.aaa", null),
            HierarchyTestData.MachineRow(id2, "machine.bbb", null)
        };

        var first = HierarchyService.BuildHierarchy(Array.Empty<WorkGroup>(), machines);
        var second = HierarchyService.BuildHierarchy(Array.Empty<WorkGroup>(), machines);

        Assert.Equal(
            first.Single(g => g.ArrayId is null).Machines.Select(m => m.Id),
            second.Single(g => g.ArrayId is null).Machines.Select(m => m.Id));
    }
}

public class HierarchyInvariantFailureTests
{
    [Fact]
    public void AssertExactMachineCoverage_ThrowsWhenMachineMissing()
    {
        var source = new[] { HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", null) };
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            Array.Empty<HierarchyService.MachineHierarchyRow>());

        var ex = Assert.ThrowsAny<Exception>(
            () => HierarchyInvariantAssertions.AssertExactMachineCoverage(source, hierarchy));

        Assert.Contains("Equal", ex.GetType().Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertNoDuplicateMachines_ThrowsWhenDuplicatePresent()
    {
        var machineId = Guid.NewGuid();
        var hierarchy = new List<HierarchyArrayDto>
        {
            new(null, HierarchyDefaults.UnassignedNameKey, new[]
            {
                new HierarchyMachineDto(machineId, "machine.one"),
                new HierarchyMachineDto(machineId, "machine.one")
            })
        };

        var ex = Assert.ThrowsAny<Exception>(
            () => HierarchyInvariantAssertions.AssertNoDuplicateMachines(hierarchy));

        Assert.Contains("more than one", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

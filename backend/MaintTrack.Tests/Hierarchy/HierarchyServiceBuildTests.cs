using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Arrays;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyServiceBuildTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ArrayAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ArrayBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void BuildHierarchy_AssignsEveryMachineExactlyOnce()
    {
        var arrays = new[]
        {
            HierarchyTestData.CreateArray(TenantId, "array.a", sortOrder: 1, id: ArrayAId),
            HierarchyTestData.CreateArray(TenantId, "array.b", sortOrder: 2, id: ArrayBId)
        };

        var machines = new[]
        {
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", ArrayAId),
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.two", ArrayBId),
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.three", null),
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.four", Guid.NewGuid())
        };

        var hierarchy = HierarchyService.BuildHierarchy(arrays, machines);

        HierarchyInvariantAssertions.AssertCompleteHierarchy(machines, hierarchy);
    }

    [Fact]
    public void BuildHierarchy_NullArrayId_GoesToUnassigned()
    {
        var machineId = Guid.NewGuid();
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            new[] { HierarchyTestData.MachineRow(machineId, "machine.unassigned", null) });

        var unassigned = Assert.Single(hierarchy);
        Assert.Null(unassigned.ArrayId);
        Assert.Equal(HierarchyDefaults.UnassignedNameKey, unassigned.NameKey);
        Assert.Equal(machineId, Assert.Single(unassigned.Machines).Id);
    }

    [Fact]
    public void BuildHierarchy_InvalidArrayId_GoesToUnassigned()
    {
        var machineId = Guid.NewGuid();
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            new[] { HierarchyTestData.MachineRow(machineId, "machine.orphan", Guid.NewGuid()) });

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(machineId, Assert.Single(unassigned.Machines).Id);
    }

    [Fact]
    public void BuildHierarchy_InactiveArrayNotInList_RoutesMachineToUnassigned()
    {
        var inactiveArrayId = Guid.NewGuid();
        var machineId = Guid.NewGuid();

        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            new[] { HierarchyTestData.MachineRow(machineId, "machine.inactiveRef", inactiveArrayId) });

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(machineId, Assert.Single(unassigned.Machines).Id);
    }

    [Fact]
    public void BuildHierarchy_ReturnsEmptyArraysWithNoMachines()
    {
        var arrays = new[]
        {
            HierarchyTestData.CreateArray(TenantId, "array.empty", sortOrder: 0, id: ArrayAId)
        };

        var hierarchy = HierarchyService.BuildHierarchy(arrays, Array.Empty<HierarchyService.MachineHierarchyRow>());

        var group = Assert.Single(hierarchy);
        Assert.Equal(ArrayAId, group.ArrayId);
        Assert.Empty(group.Machines);
        Assert.DoesNotContain(hierarchy, g => g.ArrayId is null);
    }

    [Fact]
    public void BuildHierarchy_OmitsUnassignedGroupWhenAllMachinesAreValid()
    {
        var arrays = new[]
        {
            HierarchyTestData.CreateArray(TenantId, "array.a", id: ArrayAId)
        };

        var machines = new[]
        {
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", ArrayAId)
        };

        var hierarchy = HierarchyService.BuildHierarchy(arrays, machines);

        Assert.Single(hierarchy);
        Assert.DoesNotContain(hierarchy, g => g.ArrayId is null);
    }

    [Fact]
    public void BuildHierarchy_OrdersArraysBySortOrderThenNameKey()
    {
        var arrays = new[]
        {
            HierarchyTestData.CreateArray(TenantId, "array.z", sortOrder: 1, id: ArrayBId),
            HierarchyTestData.CreateArray(TenantId, "array.a", sortOrder: 0, id: ArrayAId)
        };

        var hierarchy = HierarchyService.BuildHierarchy(
            arrays.OrderBy(a => a.SortOrder).ThenBy(a => a.NameKey).ToList(),
            Array.Empty<HierarchyService.MachineHierarchyRow>());

        Assert.Equal(ArrayAId, hierarchy[0].ArrayId);
        Assert.Equal(ArrayBId, hierarchy[1].ArrayId);
    }

    [Fact]
    public void BuildHierarchy_ReturnsTranslationKeysOnly()
    {
        var hierarchy = HierarchyService.BuildHierarchy(
            new[] { HierarchyTestData.CreateArray(TenantId, "array.line1", id: ArrayAId) },
            new[] { HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.carrotIntake", ArrayAId) });

        Assert.Equal("array.line1", hierarchy[0].NameKey);
        Assert.Equal("machine.carrotIntake", hierarchy[0].Machines[0].Name);
    }

    [Fact]
    public void BuildHierarchy_EmptyArraysAndNoMachines_ReturnsEmptyList()
    {
        var hierarchy = HierarchyService.BuildHierarchy(
            Array.Empty<WorkGroup>(),
            Array.Empty<HierarchyService.MachineHierarchyRow>());

        Assert.Empty(hierarchy);
    }

    [Fact]
    public void BuildHierarchy_MachinesExistWithoutArrays_ReturnsOnlyUnassignedGroup()
    {
        var machines = new[]
        {
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.one", null),
            HierarchyTestData.MachineRow(Guid.NewGuid(), "machine.two", Guid.NewGuid())
        };

        var hierarchy = HierarchyService.BuildHierarchy(Array.Empty<WorkGroup>(), machines);

        var unassigned = Assert.Single(hierarchy);
        Assert.Equal(2, unassigned.Machines.Count);
        HierarchyInvariantAssertions.AssertCompleteHierarchy(machines, hierarchy);
    }
}

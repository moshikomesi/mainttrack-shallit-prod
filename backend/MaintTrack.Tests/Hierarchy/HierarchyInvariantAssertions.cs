using MaintTrack.Application.Hierarchy;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Tests.Hierarchy.Support;

namespace MaintTrack.Tests.Hierarchy;

public static class HierarchyInvariantAssertions
{
    public static void AssertCompleteHierarchy(
        IReadOnlyList<HierarchyService.MachineHierarchyRow> sourceMachines,
        IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        AssertStableSchema(hierarchy);
        AssertNoNullMachines(hierarchy);
        AssertExactMachineCoverage(sourceMachines, hierarchy);
        AssertNoDuplicateMachines(hierarchy);
        AssertUnassignedGroupRules(hierarchy);
    }

    public static void AssertStableSchema(IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        foreach (var group in hierarchy)
        {
            Assert.False(string.IsNullOrWhiteSpace(group.NameKey));
            Assert.NotNull(group.Machines);

            foreach (var machine in group.Machines)
            {
                Assert.NotEqual(Guid.Empty, machine.Id);
                Assert.False(string.IsNullOrWhiteSpace(machine.Name));
            }
        }
    }

    public static void AssertNoNullMachines(IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        foreach (var group in hierarchy)
        {
            Assert.All(group.Machines, machine => Assert.NotNull(machine));
        }
    }

    public static void AssertExactMachineCoverage(
        IReadOnlyList<HierarchyService.MachineHierarchyRow> sourceMachines,
        IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        var outputIds = HierarchyTestData.FlattenMachines(hierarchy)
            .Select(m => m.Id)
            .OrderBy(id => id)
            .ToList();

        var sourceIds = sourceMachines
            .Select(m => m.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(sourceIds, outputIds);
        Assert.Equal(sourceMachines.Count, outputIds.Count);
    }

    public static void AssertNoDuplicateMachines(IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        var seen = new HashSet<Guid>();
        foreach (var group in hierarchy)
        {
            foreach (var machine in group.Machines)
            {
                Assert.True(
                    seen.Add(machine.Id),
                    $"Machine {machine.Id} appears in more than one hierarchy group.");
            }
        }
    }

    public static void AssertUnassignedGroupRules(IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        var unassignedGroups = hierarchy
            .Where(g => g.ArrayId is null)
            .ToList();

        Assert.True(
            unassignedGroups.Count <= 1,
            "Unassigned group must appear at most once.");

        foreach (var group in unassignedGroups)
        {
            Assert.Equal(HierarchyDefaults.UnassignedNameKey, group.NameKey);
        }
    }

    public static void AssertArrayOrdering(IReadOnlyList<HierarchyArrayDto> hierarchy)
    {
        var realGroups = hierarchy.Where(g => g.ArrayId is not null).ToList();
        for (var i = 1; i < realGroups.Count; i++)
        {
            var previous = realGroups[i - 1];
            var current = realGroups[i];
            Assert.True(
                string.Compare(previous.NameKey, current.NameKey, StringComparison.Ordinal) <= 0
                || realGroups.Take(i).Any(g => g.ArrayId != previous.ArrayId),
                "Real array groups must follow arrays query ordering.");
        }
    }
}

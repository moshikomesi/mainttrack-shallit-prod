using System;
using System.Collections.Generic;

namespace MaintTrack.Application.Hierarchy;

public sealed record HierarchyMachineDto(
    Guid Id,
    string Name);

public sealed record HierarchyArrayDto(
    Guid? ArrayId,
    string NameKey,
    IReadOnlyList<HierarchyMachineDto> Machines);

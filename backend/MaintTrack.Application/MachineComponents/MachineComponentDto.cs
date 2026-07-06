using System;

namespace MaintTrack.Application.MachineComponents;

public sealed record MachineComponentDto(
    Guid Id,
    string Code,
    string NameKey,
    int SortOrder);

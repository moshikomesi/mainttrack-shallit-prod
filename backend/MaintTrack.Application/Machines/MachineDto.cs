using System;

namespace MaintTrack.Application.Machines;

public sealed record MachineDto(
    Guid Id,
    string Name,
    string Code,
    string? Description
);


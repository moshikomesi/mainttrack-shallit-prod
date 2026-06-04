using System;

namespace MaintTrack.Application.Forklifts;

public sealed class CreateForkliftFaultRequest
{
    public string FaultType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal RepairCost { get; init; }
}

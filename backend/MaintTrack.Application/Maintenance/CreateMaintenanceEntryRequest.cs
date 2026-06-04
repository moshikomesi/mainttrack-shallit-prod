using System;

namespace MaintTrack.Application.Maintenance;

public sealed class CreateMaintenanceEntryRequest
{
    public Guid MachineId { get; init; }

    public DateOnly Date { get; init; }

    public Guid MaintenanceTypeId { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public string? SparePartsUsed { get; init; }

    public decimal WorkHours { get; init; }

    public bool IsSafeToOperate { get; init; }
}

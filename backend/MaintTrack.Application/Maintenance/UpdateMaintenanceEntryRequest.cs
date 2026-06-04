using System;

namespace MaintTrack.Application.Maintenance;

public sealed class UpdateMaintenanceEntryRequest
{
    public Guid MaintenanceTypeId { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public string? SparePartsUsed { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public decimal WorkHours { get; init; }

    public bool IsSafeToOperate { get; init; }
}

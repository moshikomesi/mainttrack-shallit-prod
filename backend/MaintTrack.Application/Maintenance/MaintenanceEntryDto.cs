using System;

namespace MaintTrack.Application.Maintenance;

public sealed record MaintenanceEntryDto(
    Guid Id,
    Guid MachineId,
    DateOnly Date,
    Guid? MaintenanceTypeId,
    string? MaintenanceTypeCode,
    string Description,
    string? ImageUrl,
    string? SparePartsUsed,
    string EmployeeName,
    decimal WorkHours,
    bool IsSafeToOperate,
    Guid CreatedByUserId,
    DateTime CreatedAt
);

using System;
using System.Collections.Generic;

namespace MaintTrack.Application.Maintenance;

public sealed record MaintenanceEntryDto(
    Guid Id,
    Guid MachineId,
    Guid? ArrayId,
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
    DateTime CreatedAt,
    IReadOnlyList<MaintenanceEntryImageDto> AdditionalImages
);

public sealed record MaintenanceEntryImageDto(
    Guid Id,
    string ImageUrl,
    int SortOrder
);

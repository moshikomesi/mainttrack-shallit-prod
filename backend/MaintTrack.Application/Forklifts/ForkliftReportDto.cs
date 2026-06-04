using System;
using System.Collections.Generic;

namespace MaintTrack.Application.Forklifts;

public sealed record ForkliftReportDto(
    Guid Id,
    Guid TenantId,
    Guid ForkliftId,
    DateOnly ReportDate,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    IReadOnlyList<ForkliftTreatmentDto> Treatments,
    IReadOnlyList<ForkliftFaultDto> Faults,
    IReadOnlyList<ForkliftInspectionDto> Inspections
);

public sealed record ForkliftTreatmentDto(
    Guid Id,
    Guid ReportId,
    DateOnly Date,
    string Description,
    string Technician
);

public sealed record ForkliftFaultDto(
    Guid Id,
    Guid ReportId,
    string FaultType,
    string Description,
    decimal RepairCost
);

public sealed record ForkliftInspectionDto(
    Guid Id,
    Guid ReportId,
    DateOnly TestDate,
    DateOnly ExpiryDate
);

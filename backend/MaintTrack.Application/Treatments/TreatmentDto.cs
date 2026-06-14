using System;

namespace MaintTrack.Application.Treatments;

public sealed record TreatmentDto(
    Guid Id,
    Guid? MachineId,
    string? MachineName,
    DateOnly TreatmentDate,
    Guid? MaintenanceTypeId,
    string? MaintenanceTypeName,
    string Description,
    string Technician,
    DateOnly? NextDueDate,
    Guid? CreatedByUserId
);

using System;
using MaintTrack.Domain.Treatments;

namespace MaintTrack.Application.Treatments;

public sealed record TreatmentDto(
    Guid Id,
    EquipmentType EquipmentType,
    DateOnly TreatmentDate,
    TreatmentType TreatmentType,
    string Description,
    string Technician,
    decimal Cost,
    DateOnly? NextDueDate
);

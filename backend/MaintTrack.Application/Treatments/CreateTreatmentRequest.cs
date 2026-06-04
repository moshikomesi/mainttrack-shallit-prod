using System;
using MaintTrack.Domain.Treatments;

namespace MaintTrack.Application.Treatments;

public sealed class CreateTreatmentRequest
{
    public EquipmentType EquipmentType { get; init; }

    public DateOnly TreatmentDate { get; init; }

    public TreatmentType TreatmentType { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Technician { get; init; } = string.Empty;

    public decimal Cost { get; init; }

    public DateOnly? NextDueDate { get; init; }
}

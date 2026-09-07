using System;

namespace MaintTrack.Application.Treatments;

public sealed class CreateTreatmentRequest
{
    public Guid MachineId { get; init; }

    public DateOnly TreatmentDate { get; init; }

    public Guid MachineComponentId { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Technician { get; init; } = string.Empty;

    public DateOnly? NextDueDate { get; init; }
}

using System;

namespace MaintTrack.Application.Forklifts;

public sealed class CreateForkliftTreatmentRequest
{
    public DateOnly Date { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Technician { get; init; } = string.Empty;
}

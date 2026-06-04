using System;
using System.Collections.Generic;

namespace MaintTrack.Application.Forklifts;

public sealed class CreateForkliftReportRequest
{
    public Guid ForkliftId { get; init; }

    public DateOnly ReportDate { get; init; }

    public IReadOnlyList<CreateForkliftTreatmentRequest> Treatments { get; init; } = Array.Empty<CreateForkliftTreatmentRequest>();

    public IReadOnlyList<CreateForkliftFaultRequest> Faults { get; init; } = Array.Empty<CreateForkliftFaultRequest>();

    public IReadOnlyList<CreateForkliftInspectionRequest> Inspections { get; init; } = Array.Empty<CreateForkliftInspectionRequest>();
}

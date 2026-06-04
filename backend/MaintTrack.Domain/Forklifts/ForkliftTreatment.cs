using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Forklifts;

/// <summary>
/// A treatment record attached to a forklift report.
/// </summary>
public class ForkliftTreatment : BaseEntity
{
    public Guid ReportId { get; set; }

    public DateOnly Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Technician { get; set; } = string.Empty;

    public ForkliftReport Report { get; set; } = null!;
}

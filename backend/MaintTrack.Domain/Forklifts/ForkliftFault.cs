using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Forklifts;

/// <summary>
/// A fault record attached to a forklift report.
/// </summary>
public class ForkliftFault : BaseEntity
{
    public Guid ReportId { get; set; }

    public string FaultType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal RepairCost { get; set; }

    public ForkliftReport Report { get; set; } = null!;
}

using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Forklifts;

/// <summary>
/// Aggregate root for forklift maintenance reports (treatments, faults, inspections).
/// </summary>
public class ForkliftReport : TenantEntity
{
    public Guid ForkliftId { get; set; }

    public Forklift? Forklift { get; set; }

    public DateOnly ReportDate { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<ForkliftTreatment> Treatments { get; set; } = new List<ForkliftTreatment>();

    public ICollection<ForkliftFault> Faults { get; set; } = new List<ForkliftFault>();

    public ICollection<ForkliftInspection> Inspections { get; set; } = new List<ForkliftInspection>();
}

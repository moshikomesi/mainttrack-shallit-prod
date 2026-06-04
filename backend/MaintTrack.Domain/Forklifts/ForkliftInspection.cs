using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Forklifts;

/// <summary>
/// An inspection record attached to a forklift report. ExpiryDate must be greater than TestDate.
/// </summary>
public class ForkliftInspection : BaseEntity
{
    public Guid ReportId { get; set; }

    public DateOnly TestDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public ForkliftReport Report { get; set; } = null!;
}

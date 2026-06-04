using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Forklifts;

/// <summary>
/// A forklift asset belonging to a tenant. Has many ForkliftReports.
/// </summary>
public class Forklift : TenantEntity
{
    public string LicenseNumber { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Date of the last inspection (current certification state).
    /// </summary>
    public DateTime? LastInspectionDate { get; set; }

    /// <summary>
    /// Date when the current inspection expires.
    /// </summary>
    public DateTime? InspectionExpiryDate { get; set; }

    /// <summary>
    /// When the inspection fields were last updated.
    /// </summary>
    public DateTime? InspectionUpdatedAt { get; set; }

    public ICollection<ForkliftReport> Reports { get; set; } = new List<ForkliftReport>();
}

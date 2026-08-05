using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Maintenance;

public class MaintenanceEntry : TenantEntity
{
    public Guid MachineId { get; set; }

    public DateOnly Date { get; set; }

    public Guid? MaintenanceTypeId { get; set; }

    public MaintenanceType? MaintenanceType { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public ICollection<MaintenanceEntryImage> AdditionalImages { get; set; } = new List<MaintenanceEntryImage>();

    public string? SparePartsUsed { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public decimal WorkHours { get; set; }

    public bool IsSafeToOperate { get; set; }

    public Guid CreatedByUserId { get; set; }
}

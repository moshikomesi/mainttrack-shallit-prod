using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class AnnualPlanItem : BaseEntity
{
    public Guid PlanId { get; set; }

    public AnnualPlan Plan { get; set; } = null!;

    public Guid TaskId { get; set; }

    public MaintenanceTask Task { get; set; } = null!;

    public ICollection<PreventivePlanDate> Dates { get; set; } = new List<PreventivePlanDate>();

    public SummerPlanExecution? Execution { get; set; }
}


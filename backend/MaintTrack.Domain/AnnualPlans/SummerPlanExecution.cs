using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class SummerPlanExecution : BaseEntity
{
    public Guid PlanItemId { get; set; }

    public AnnualPlanItem PlanItem { get; set; } = null!;

    public DateOnly? PlannedStart { get; set; }

    public int? RequiredDays { get; set; }

    public DateOnly? PlannedFinish { get; set; }

    public string? Description { get; set; }

    public DateOnly? ActualStart { get; set; }

    public DateOnly? ActualFinish { get; set; }

    public ICollection<SummerPlanWorker> Workers { get; set; } = new List<SummerPlanWorker>();
}


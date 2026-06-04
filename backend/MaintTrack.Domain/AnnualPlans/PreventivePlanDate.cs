using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class PreventivePlanDate : BaseEntity
{
    public Guid PlanItemId { get; set; }

    public AnnualPlanItem PlanItem { get; set; } = null!;

    public DateOnly ScheduledDate { get; set; }
}


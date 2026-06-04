using System;
using System.Collections.Generic;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class AnnualPlan : TenantEntity
{
    public int Year { get; set; }

    public PlanType Type { get; set; }

    public ICollection<AnnualPlanItem> Items { get; set; } = new List<AnnualPlanItem>();
}


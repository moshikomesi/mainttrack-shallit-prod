using System;
using System.Collections.Generic;
using MaintTrack.Domain.AnnualPlans;

namespace MaintTrack.Application.AnnualPlans;

public sealed class CreateAnnualPlanRequest
{
    public int Year { get; init; }

    public PlanType Type { get; init; }

    public IReadOnlyCollection<CreateAnnualPlanItemRequest> Items { get; init; } = Array.Empty<CreateAnnualPlanItemRequest>();
}


using System;
using System.Collections.Generic;
using MaintTrack.Domain.AnnualPlans;

namespace MaintTrack.Application.AnnualPlans;

public sealed record AnnualPlanDto(
    Guid Id,
    int Year,
    PlanType Type,
    IReadOnlyCollection<AnnualPlanItemDto> Items);


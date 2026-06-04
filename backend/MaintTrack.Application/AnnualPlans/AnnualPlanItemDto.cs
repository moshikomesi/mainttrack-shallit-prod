using System;
using System.Collections.Generic;

namespace MaintTrack.Application.AnnualPlans;

public sealed record AnnualPlanItemDto(
    string TaskKey,
    IReadOnlyCollection<DateOnly> Dates,
    SummerExecutionDto? Execution);


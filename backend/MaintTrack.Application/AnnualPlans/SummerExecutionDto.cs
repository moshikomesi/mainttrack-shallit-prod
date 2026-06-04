using System;
using System.Collections.Generic;

namespace MaintTrack.Application.AnnualPlans;

public sealed record SummerExecutionDto(
    DateOnly? PlannedStart,
    int? RequiredDays,
    DateOnly? PlannedFinish,
    string? Description,
    DateOnly? ActualStart,
    DateOnly? ActualFinish,
    IReadOnlyCollection<string> Workers);


using System;
using System.Collections.Generic;

namespace MaintTrack.Application.AnnualPlans;

public sealed class SummerExecutionRequest
{
    public DateOnly? PlannedStart { get; init; }

    public int? RequiredDays { get; init; }

    public DateOnly? PlannedFinish { get; init; }

    public string? Description { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? ActualFinish { get; init; }

    public IReadOnlyCollection<Guid> WorkerIds { get; init; } = Array.Empty<Guid>();
}


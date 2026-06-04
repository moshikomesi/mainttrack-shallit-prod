using System;
using System.Collections.Generic;

namespace MaintTrack.Application.AnnualPlans;

public sealed class CreateAnnualPlanItemRequest
{
    public Guid TaskId { get; init; }

    /// <summary>
    /// Dates for preventive plans. Ignored for summer plans.
    /// </summary>
    public IReadOnlyCollection<DateOnly>? Dates { get; init; }

    /// <summary>
    /// Execution details for summer plans. Must be null for preventive plans.
    /// </summary>
    public SummerExecutionRequest? Execution { get; init; }
}


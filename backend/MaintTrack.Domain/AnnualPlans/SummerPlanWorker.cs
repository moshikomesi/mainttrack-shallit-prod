using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class SummerPlanWorker : BaseEntity
{
    public Guid ExecutionId { get; set; }

    public SummerPlanExecution Execution { get; set; } = null!;

    public Guid TechnicianId { get; set; }

    public Technician Technician { get; set; } = null!;
}


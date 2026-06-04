using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Domain.AnnualPlans;

namespace MaintTrack.Application.AnnualPlans;

public interface IAnnualPlanService
{
    Task<Guid> CreateOrUpdateAsync(CreateAnnualPlanRequest request, CancellationToken ct);

    Task<AnnualPlanDto?> GetByYearAsync(int year, PlanType type, CancellationToken ct);

    Task<IEnumerable<AnnualPlanDto>> GetListAsync(int? year, PlanType? type, CancellationToken ct);
}


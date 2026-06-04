using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class MaintenanceTask : TenantEntity
{
    public PlanType Type { get; set; }

    public string TranslationKey { get; set; } = string.Empty;

    public int OrderIndex { get; set; }
}

